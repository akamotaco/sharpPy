# p33 — `memoryview.cast` / iteration + re flags unblocked

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Why this mattered

Any `re` call with a non-zero `flags=` argument crashed:

```
AttributeError: 'memoryview' object has no attribute 'cast'
```

`Lib/re/_compiler.py:398` (`_bytes_to_codes`) builds the BIGCHARSET word array with
`memoryview(b).cast('I')`, then asserts `itemsize == _sre.CODESIZE` and returns
`.tolist()`. Because SharpPy runs the pure-Python `re` compiler during
`re.compile`, the missing `cast` aborted compilation before matching ever
happened — so `IGNORECASE`, `MULTILINE`, `DOTALL`, `VERBOSE`, and flag-using
`sub` were all dead, not just one of them.

## CPython semantics measured (little-endian host)

`mv.cast(fmt)` reinterprets the buffer's raw bytes as native elements:
- `itemsize` = sizeof(fmt) (`'B'`=1, `'H'`=2, `'I'`/`'i'`/`'l'`/`'L'`=4,
  `'q'`/`'Q'`=8, `'f'`=4, `'d'`=8); `'c'`=1.
- Requires the byte length to be a multiple of the new itemsize, else
  `TypeError: memoryview: length is not a multiple of itemsize` (checked before
  shape).
- `len` = total_bytes // itemsize; iteration / indexing / `tolist()` decode
  elements little-endian; `tolist()` returns a **list**.
- Optional shape: validates `product(shape) * itemsize == total_bytes`, else
  `TypeError: memoryview: product(shape) * itemsize != buffer size`.
- Invalid/unsupported format →
  `ValueError: memoryview: destination format must be a native single character
  format prefixed with an optional '@'`.
- Byte-view `format` is `'B'` (not the empty string).

## Implementation (`core/PyBuffer.cs`)

- Added a `_fmt` field (CPython `Py_buffer.format`, default `'B'`) threaded through
  every constructor, the slice child, `GetBuffer`, and `ExportedView`.
- `ReadElement(storageByte)` decodes one element by `_fmt` (signed/unsigned ints
  little-endian, `'f'`/`'d'` via `BitConverter`, `'c'` as a 1-byte `bytes`).
  Byte views (`'B'`) decode exactly as before, so existing behaviour is unchanged.
- `Data` (raw byte span) now spans `_length * _itemsize` and gathers per element;
  the `_itemsize == 1` fast path is preserved.
- `tolist()` and integer `__getitem__` route through `ReadElement`; `tolist`
  returns a `PyList` (was a tuple — a latent bug).
- New `cast` descriptor (1 or 2 positional args, kwargs rejected, optional
  `@/=/</>` byte-order prefix) with the measured CPython messages; returns a new
  C-contiguous `Reinterpret(fmt, itemsize)` view.
- Added `__iter__` descriptor **and** a `GetIterator()` override so
  `iter(memoryview(...))` / `list(...)` yield elements through the VM protocol
  (previously `'memoryview' object is not iterable`).
- Added `format` / `nbytes` / `ndim` / `shape` attributes.

## Verification

- `tests/bughunt/p33_memoryview_cast.py`: stdout diff `0` (cast B/H/I/i/signed-h,
  misalignment, shape error, ndim/nbytes/format, `tolist`, byte + cast iteration,
  bytearray cast, cast→tobytes, bad-format message) **and** the full `re` flags
  matrix (`IGNORECASE` findall/match, `MULTILINE`, `DOTALL`, `VERBOSE`, `sub`
  `IGNORECASE`, named-group + `IGNORECASE`).
- `tools/run_bughunt.py`: **40/40 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: flagged `str_fmt +25% / list_comp +79%`; same binary
  re-measured `str_fmt +0.5%/+10.2%`, `list_comp -0.4%/+10.7%` → machine noise
  (the change touches memoryview and the re compile path only).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residuals

- The `>` / `!` (big-endian) byte-order prefix is accepted but not byte-swapped
  (host is little-endian and nothing exercised it); `cast` returns a flat view —
  true N-dimensional memoryviews (`ndim > 1`) are not modelled.
- `memoryview` `__eq__`, multi-dim `cast` shapes, and `struct`-format round-trips
  were not in scope; deferred.
