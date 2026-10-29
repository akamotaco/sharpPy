# p45 — memoryview equality and hashing (`memory_richcompare` / `memory_hash`)

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-4 and P0-15

## C source first (`cpython-v3.12.15`)

`Objects/memoryobject.c`:

* `memory_richcompare` — only `Py_EQ`/`Py_NE`; anything else returns `NotImplemented`.
  A non-memoryview operand goes through `PyObject_GetBuffer(w, &wbuf, PyBUF_FULL_RO)`, and a
  failure is cleared → `NotImplemented` (so Python falls back to identity).
  `equiv_shape(vv, ww)` compares **`ndim` and per-axis element counts**; unequal shapes are
  simply unequal, not an error. Then, in a comment worth quoting:

  > NOTE: Even for equal format strings, memcmp() cannot be used for item comparison since it
  > would give incorrect results in the case of NaNs or uninitialized padding bytes.

  so items are compared **by decoded value**.
* `memory_hash` — in order: `CHECK_RELEASED_INT`, then `!view->readonly` →
  `ValueError: cannot hash writable memoryview object`, then `get_native_fmtchar`/
  `IS_BYTE_FORMAT` → `ValueError: memoryview: hashing is restricted to formats 'B', 'b' or 'c'`,
  then the hash of the byte content.

## Measured divergence (32 of 38 lines)

| expression | CPython | SharpPy before |
|---|---|---|
| `memoryview(b"abc") == memoryview(b"abc")` | `True` | `False` |
| `memoryview(b"abc") == b"abc"` / `== bytearray(b"abc")` | `True` | `False` |
| `b"abc" == memoryview(b"abc")` (reflected) | `True` | `False` |
| `memoryview(bytearray(b"xxabcxx"))[2:5] == memoryview(b"abc")` | `True` | `False` |
| `hash(mv) == hash(b"abc")` | `True` | `False` |
| `{b"abc": 1}[memoryview(b"abc")]` | `1` | **`KeyError`** |
| `hash(memoryview(b"ab").cast("I"))` | `ValueError: memoryview: hashing is restricted to formats 'B', 'b' or 'c'` | returned a CLR number |
| `hash(memoryview(bytearray(b"abc")))` | `ValueError: cannot hash writable memoryview object` | returned `False`/a number |
| `hash(released_view)` | `ValueError: operation forbidden on released memoryview object` | returned a number |
| `memoryview(NaN8).cast("d") == itself` | `False` (NaN) | n/a |
| `cast("f") == cast("d")` (4 vs 8 bytes, both shape (1,)) | `True` for 0.0 | n/a |

Non-divergences kept as regression cover: `== "abc"` / `== 5` / `== [97,98,99]` / `== True` are
`False`; `<`, `<=`, `>` raise `TypeError`; `cast("I") == cast("H")` is `False` (shapes `(1,)`
vs `(2,)`); `cast("b")` of `b"\xff"` (−1) is not `cast("B")` (255).

## C → C# translation

* `PyMemoryView.PyEquals` implements the same shape, returning `PyNotImplemented.Instance`
  for buffer-less operands so the existing reflected/identity fallback stays in charge.
  `bytes` and `bytearray` already returned `NotImplemented` for memoryview, matching
  `bytes_richcompare`, so both directions work once the view answers.
* Element decoding reuses the p42 `ReadElement(StorageIndex(i))` pair, which already honours
  the byte stride, so sliced and N-D views compare in logical row-major order.
* `equiv_shape` uses `_shape ?? new[]{ _length }`: `itemsize` is deliberately **not** part of
  the shape, and this is why a 4-byte `'f'` view and an 8-byte `'d'` view can be equal.
* Hashing: the earlier ledger note said this needed a new hook because "CLR `GetHashCode`
  must not throw". SharpPy already has the right seam — `PyObject.ToHash()` is the Python
  hash hook and `GetHashCode() => ToHash()` — so the guard lives in a `ToHash()` override,
  and byte views return `new PyBytes(Data).ToHash()` so view and bytes agree in SharpPy's own
  hash space (CPython's siphash values are not reproducible and are not compared).

## The regression this exposed (and the rule it teaches)

`tools/evaluate.py` dropped to `224 matching_output / 2 sharppy_failure`, while the new probe
was green. Both failures came from `PyMemoryView.ToString()`:

```
print(f"  Memoryview slice: {mv[0:5]}")   → ValueError: cannot hash writable memoryview object
assert 'released memory' in repr(view)    → ValueError: operation forbidden on released …
```

because it formatted identity with `GetHashCode()`, which now routes into the throwing
`ToHash()`. CPython `memory_repr` never raises. Fixed by taking
`RuntimeHelpers.GetHashCode(this)` directly. **Rule:** a throwing `ToHash()` makes
`GetHashCode()` throw — correct for `hash()`/dict lookups, wrong for identity formatting.
Any type that gains a restricted `ToHash()` must stop using `GetHashCode()` in `ToString()`.

## Verification

- `tests/bughunt/p45_memoryview_eq.py`: 51 lines, stdout diff **0** (addresses normalized,
  and hash values only compared against the matching `bytes`, never across interpreters).
- Supporting matrix `.artifacts/tmp/mveq.py` / `mveq2.py`: 32 → 0 divergences
  (`struct` is unavailable in SharpPy, so the NaN case uses literal bytes).
- `tools/run_bughunt.py`: **50/50 matching**.
- `tools/evaluate.py`: restored to `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: runs flagged `str_fmt +20 %` / `list_comp +94 %`; same-binary re-runs
  gave `list_comp −2.3 % / +0.4 % / +33 %` and `dict_ops +0.2 % / +0.2 % / −0.5 %` — the dict
  path is flat, and the spikes are the recurring machine noise seen in p26/p29/p30/p32/p33/
  p40/p41/p44.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Still open

- **P1-8**: `hasattr(memoryview(b"a"), '__eq__' / '__ne__' / '__hash__' / '__buffer__')` is
  still `False`; the behaviour is now right through CLR virtuals, only the visible slot
  wrappers are missing (kept separate because materializing `__eq__` descriptors broke CLR
  dispatch in p22).
- `memoryview` of a non-readonly export hashing aside, `struct` module absence (P2 stdlib)
  surfaced while writing the probe.
