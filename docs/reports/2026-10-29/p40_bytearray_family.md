# p40 — bytearray search/query family + two empty-subsection P0s

Date: 2026-10-29
Branch: `work/bug-hunt`

## What the measurement showed

A surface probe over ~50 byte methods found SharpPy's `bytearray` was far thinner than
CPython's (which legitimately lacks only `isprintable`):

| | CPython missing | SharpPy missing |
|---|---|---|
| byte-method surface | 1 (`isprintable`) | **41** |

This round implemented the search/query family named by P0-11; the rest is registered as
P0-14 with the exact measured list.

## Implemented (`type/PyByteArray.cs`, one shared core)

`count, index, rfind, rindex, startswith, endswith, removeprefix, removesuffix` were
raising `AttributeError`; `find` ignored non-integer start/stop. All nine now route
through one CPython-shaped core (`ParseRange` / `SubArg` / `SameAt` / `FindCore` /
`CountCore` / `StartEndCore` / `TrimEdgeCore`), reusing `PyBytes.BytesIndex` for
`__index__`:

- `int` 0..255 or bytes-like subsection; out-of-range → `ValueError: byte must be in range(0, 256)`;
  wrong type → `argument should be integer or bytes-like object, not 'str'`
- `startswith`/`endswith` accept bytes-like **or a tuple** of them, with the two distinct
  CPython messages (`a bytes-like object is required, not 'str'` vs
  `startswith first arg must be bytes or a tuple of bytes, not int`)
- missing `index`/`rindex` → `ValueError: subsection not found`
- empty subsection matches at the range boundary (`find("")`, `rfind("")`)
- arity texts verified against CPython: `find() takes at least 1 argument (0 given)`,
  `find() takes at most 3 arguments (4 given)`,
  `bytearray.removesuffix() takes exactly one argument (2 given)` (METH_O form)

## Two P0s found while chasing the hang

1. **`bytes.count(b"")` hung the interpreter.** The non-overlap step
   `i += sub.Value.Length - 1` decrements forever when the subsection is empty. CPython
   returns `end - start + 1`. (`b"hello world".count(b"") == 12`.)
2. **`str.count("")` returned 0** instead of counting boundary positions, and the
   descriptor path ignored the range. Now `max(0, end - start + 1)` on both the
   `TypeDict` descriptor and the private `PyString.Count` fast path
   (`"hello world".count("", 2, 5) == 4`, `.count("", 5, 5) == 1`).

Empty-subsection parity now holds across `bytes`, `bytearray`, and `str`, including
negative, reversed, and out-of-range bounds.

## Verification

- `tests/bughunt/p40_bytearray_family.py`: 62 lines, stdout diff `0`.
- Surface probe: missing byte methods 41 → 33 (the remainder is P0-14).
- `tools/run_bughunt.py`: **45/45 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: one run flagged `closure_calls +30.4%` / `comp_lambda +16.5%`;
  three same-binary re-runs gave `+5.0% / -0.7% / +11.9%` and `+6.3% / +6.3% / +7.3%`
  → machine noise, and neither path touches this change.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residuals registered

- **P0-13**: `bytes` still rejects an `int` subsection, lacks tuple prefixes, and uses
  `a bytes-like object is required…` where CPython says
  `argument should be integer or bytes-like object…`. The bytearray core is now the
  reference to mirror.
- **P0-14**: 33 remaining `bytearray` methods (measured list in `docs/known_gaps.md`).
