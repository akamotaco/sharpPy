# p35 — `int(float)` arbitrary-precision truncation (P0-1)

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured bug (execution divergence)

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `int(1e17)` | `100000000000000000` | `OverflowError: int too big to convert to float` |
| `int(1e30)` | `1000000000000000019884624838656` | `OverflowError` |
| `int(2.0**40)` | `1099511627776` | `OverflowError` |

Small floats worked (`int(1e9)`); anything above `int.MaxValue` failed.

## Root cause

`int(x)` → `PyInt.FromArguments` fell through to `value.AsInt()`, and for a
`PyFloat` that reaches `PyFloat.ToInt()` which truncates to a C# **Int32** and
raises `OverflowError` when `Value > int.MaxValue`. `int()` must produce an
arbitrary-precision integer, not an Int32.

## Fix (`type/PyInt.cs`)

Added a `PyFloat` branch in `FromArguments` before the `AsInt()` fallback:
- `NaN` → `ValueError: cannot convert float NaN to integer`
- `±inf` → `OverflowError: cannot convert float infinity to integer`
- otherwise `new BigInteger(double)`, which truncates toward zero and reproduces
  CPython's exact-binary-value conversion (`int(1e30)` == the exact double value).

`PyFloat.ToInt()` itself is unchanged (internal callers still expect the Int32
range helper); only the `int()` builtin path was wrong.

## Verification

- `tests/bughunt/p35_int_float.py`: stdout diff `0` (1e17 / 1e9 / 2**40 / 1e30 /
  -2.7 / -0.9 / 0.0 / pi / 1e18 / 2**53+2.0 / inf / -inf / nan).
- `tools/run_bughunt.py`: **42/42 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Ledger

`docs/known_gaps.md` **P0-1** closed. Next P0 in priority order: P0-2 (`(?P=name)`
re backref → wrong matches), P0-3 (bytes find/count arg check), P0-4
(`memoryview.__eq__`).
