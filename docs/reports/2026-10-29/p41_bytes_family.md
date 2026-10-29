# p41 — bytes argument protocol; byte search/query core unified in `PyBytes`

Date: 2026-10-29
Branch: `work/bug-hunt`
Follows: `docs/reports/2026-10-29/p40_bytearray_family.md`

## Measured divergence (27 items, all on the `bytes` side)

CPython `Objects/bytesobject.c` treats these members as one family; SharpPy's `bytes`
implementation had grown nine independent bodies, each with its own argument handling:

| call | CPython | SharpPy before |
|---|---|---|
| `b"hello world".find(119)` | `6` | `TypeError: a bytes-like object is required, not 'int'` |
| `.find(255)` | `-1` | `TypeError` |
| `.find(300)` / `.find(-1)` | `ValueError: byte must be in range(0, 256)` | `TypeError` |
| `.find(bytearray(b"wo"))` | `6` | `TypeError` |
| `.find(memoryview(b"wo"))` | `6` | `TypeError` |
| `.find("o")` | `argument should be integer or bytes-like object, not 'str'` | `a bytes-like object is required, not 'str'` |
| `.startswith((b"he", b"x"))` | `True` | `TypeError: … not 'tuple'` |
| `.startswith(104)` | `startswith first arg must be bytes or a tuple of bytes, not int` | `a bytes-like object is required, not 'int'` |
| `.startswith("he")` | `… not str` (no quotes) | `… not 'str'` |
| `.removeprefix("h")` / `(104)` | `a bytes-like object is required, not 'str'` / `not 'int'` | `removeprefix() argument must be bytes` |
| `.removeprefix(b"a", b"b")` | `bytes.removeprefix() takes exactly one argument (2 given)` | `removeprefix() takes exactly 1 argument (2 given)` |
| `.find(b"o", 1, 2, 3)` | `find() takes at most 3 arguments (4 given)` | `find() takes at least 1 argument (4 given)` |

## Implementation

Moved the p40 core from `PyByteArray` to **`PyBytes`** — the same split CPython uses
(`bytearrayobject.c` delegates to `bytesobject.c`) — and generalised it:

- `SubArg(arg, allowInt)`: `int` bytes are legal only for the find/count family;
  prefixes/suffixes are bytes-like only, which is why the two cases have different
  CPython messages. `bytes`, `bytearray` and `memoryview` all qualify (via
  `PyMemoryView.Data`).
- `FindCore / CountCore / StartEndCore / TrimEdgeBytes` now take
  `IReadOnlyList<byte>`, so `byte[]` (bytes) and `List<byte>` (bytearray) share them
  without copying.
- All nine `bytes` descriptors became one-line delegations through a `Bts` dispatcher
  carrying the measured arity texts, including the METH_O form
  `bytes.NAME() takes exactly one argument (N given)`.
- `TrimEdgeBytes` returns the byte buffer and each owner wraps it, so `bytes` returns
  `bytes` and `bytearray` returns `bytearray`.

Net diff: **176 insertions, 525 deletions** (−349 lines) across `type/PyBytes.cs` and
`type/PyByteArray.cs`. Removed duplication, not features: 9 hand-rolled bodies → 1 core.

## Verification

- `tests/bughunt/p41_bytes_family.py`: 64 lines, stdout diff `0` (int bytes, out-of-range,
  bytearray/memoryview args, tuples, empty-tuple, boundary empty subsection, arity texts,
  plus bytearray regression through the same core).
- Prior matrices re-run unchanged: `b13`, `b13c`, `empt`, `empt2`, `ba`, `ba2` — all diff `0`.
- `tools/run_bughunt.py`: **46/46 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: `str_fmt +0.0%`, `list_comp −1.7%`; one run flagged
  `closure_calls +23.4%`, three same-binary re-runs gave `+10.4% / +7.6% / −1.5%`
  → machine noise (this change touches no closure path).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Remaining byte-family work

- **P0-14**: 33 `bytearray` methods still missing (`split` family, `strip` family, case
  conversions, predicates, `join/replace/hex/decode/zfill/partition/expandtabs/maketrans/
  fromhex` and justify members).
- Not modelled yet: `memoryview` with a non-byte format used as a search argument, and
  true buffer-export through `PyObject_GetBuffer` for user types (`__buffer__`).
