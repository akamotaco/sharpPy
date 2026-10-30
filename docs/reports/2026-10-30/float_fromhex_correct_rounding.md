# p69 — `float.fromhex` is CPython's correctly rounded decoder (P0-32)

Date: 2026-10-30 · branch `work/bug-hunt` · reference CPython 3.12.15 (commit-pinned clone)

## The symptom

```python
float.fromhex("0x1p3")   # CPython 8.0    SharpPy 0.0
float.fromhex("12")      # CPython 18.0   SharpPy 1.0
float.fromhex("0x1p3x")  # CPython ValueError   SharpPy 0.0
float.fromhex("0x1.921fb54442d18p+1")   # both 3.141592653589793
```

A silent wrong value, plus a missing error. The implementation existed and had a CPython line-number
comment — but the comment named `float_fromhex_core`, which is a later CPython's function name; in
3.12 the function is `float_fromhex` at `Objects/floatobject.c:1311-1549`. Reading that source is what
turned a two-line "fix the loops" patch into the correct one.

## What the C source says

```text
Objects/floatobject.c:1376-1379   leading whitespace: while (Py_ISSPACE(*s))   (ASCII only)
Objects/floatobject.c:1382-1385   _Py_parse_inf_or_nan consumes its own sign, then jumps to finished
Objects/floatobject.c:1396-1404   [0x] only if a literal '0' is followed by x/X
Objects/floatobject.c:1406-1417   coeff: <hex> [. <hex>]; coeff_end = s-1 in the dotted case
Objects/floatobject.c:1424-1426   ndigits limit -> "hexadecimal string too long to convert"
Objects/floatobject.c:1429-1440   [p [+-] <dec digits>]
Objects/floatobject.c:1462-1482   strip leading zeros; underflow to 0.0; exp > LONG_MAX/2 -> overflow
Objects/floatobject.c:1500-1540   bit extraction + round-half-even, incl. the rounding overflow corner
Objects/floatobject.c:1541-1548   trailing whitespace must reach the end of the string
```

The important part is the last third: CPython does **not** accumulate hex digits into a double and
scale. It finds the exponent of the least significant bit of the *rounded* result, keeps the digits
that matter, masks the key digit, and applies round-half-even. The naive version cannot express this:

| input | CPython | naive accumulate-then-ScaleB |
|---|---|---|
| `0x1.00000000000008p+0` | `1.0` (tie rounds to even) | loses the bit, indistinguishable |
| `0x1.00000000000018p+0` | `1.0000000000000004` | same value as the row above |
| `0x1.fffffffffffff8p+1023` | `OverflowError` | silently `inf` |

## What changed

`PyFloat.ParseHexFloat` is a transliteration of `float_fromhex`, including:

- ASCII-only whitespace, and only leading/trailing (`float.fromhex("\u00a00x1p3")` → `ValueError`,
  `"- 0x1p3"` → `ValueError`, `"0x1.0 p+2"` → `ValueError`; the old code used `Trim()`, which strips
  NBSP, and never checked for leftovers).
- `inf` / `infinity` / `nan` with optional sign, and *no* trailing junk (`nan)abc` → `ValueError`).
- the three distinct error texts, without invented suffixes:
  `invalid hexadecimal floating-point string`, `hexadecimal value too large to represent as a float`,
  `hexadecimal string too long to convert`.
- saturation for absurd exponents (`0x1p99999999999999999999`) mirroring `strtol`.
- rebuilding through the class the classmethod was reached from, so
  ```python
  class G(float):
      def __new__(cls, value=0.0):
          if value == 8.0:
              raise MyErr("hooked")
          return super().__new__(cls, value)
  G.fromhex("0x1p3")     # MyErr: hooked
  ```
  raises instead of quietly producing a `G`. The first attempt used `CreateInstance`, which bypasses
  `__new__`; the probe caught it.

## Gates

* `tests/bughunt/p69_float_hex.py` — 48 named cases + 31 fixed hex strings round-tripped
  (`float.fromhex(h).hex() == h`), diff **0**. No `struct` import: SharpPy has no `struct` module, so
  the reference hex strings are literals generated from CPython.
* `tools/run_bughunt.py`: **74/74**. `tools/evaluate.py`: unchanged `225 / 3 / 2`.
* Godot 4.7.2 mono: 0 errors, `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`.
* `tools/perf_probe.py` again flagged `str_fmt +22.0%` / `list_comp +74.8%`. `float.fromhex` is not
  reachable from any workload in the suite. In isolation, the new binary runs the `list_comp`
  workload at `0.1148 / 0.1121 / 0.1148 / 0.1185 / 0.1180`, the same distribution as both binaries
  measured an hour earlier (`0.1123…0.1168`). The suite's baseline for these two workloads was
  captured while the machine was in a faster mode, so it is currently measuring machine state, not
  code. Re-anchoring `.artifacts/perf/baseline.json` is registered as housekeeping.
