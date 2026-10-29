# p38 — bytes find/count/index/rfind/rindex `__index__` validation (P0-3, bytes part)

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured bug (execution divergence)

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `b"hello world".find(b"o","x")` | `TypeError: slice indices must be integers or None or have an __index__ method` | `4` (bad arg silently ignored) |
| `b"…".count(b"o","x")` | `TypeError` | `2` |
| `b"…".index(b"o","x")` | `TypeError` | `4` |
| `b"…".rfind(b"o","x")` / `.rindex` | `TypeError` | value |

CPython parses every start/stop with `PyNumber_Index`: int/bool/`__index__` accepted,
`None` means the default, anything else is `TypeError`. SharpPy used
`args[1] is PyInt` and dropped non-`PyInt` silently.

## Fix (`type/PyBytes.cs`)

- Added `BytesIndex(PyObject)` — int/bool via `TryGetIntArg`, else `PyObject.TryGetIndex`
  (`__index__`), else CPython `TypeError`.
- Replaced the shared `start`/`end` idiom in **all 7 descriptor sites** (find, count,
  index, rfind, rindex, + 2 others) so `None` keeps the default and any other non-index
  raises. Single shared block → one consistent behavior across bytes methods.

## Verification

- `tests/bughunt/p38_bytes_index.py`: stdout diff `0` (bad/None/True/int/start-stop,
  negative, and a custom `__index__` object).
- `tools/run_bughunt.py`: **43/43 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Split residual

`bytearray` was measured to still ignore non-int start/stop **and** `bytearray.count`
is missing entirely (`AttributeError`). Tracked as **P0-11** (reuse `PyBytes.BytesIndex`,
port validation + add `count`/`index` to `PyByteArray`). Not in this commit.
