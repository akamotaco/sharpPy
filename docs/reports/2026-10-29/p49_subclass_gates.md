# p49 — builtin-subclass gates (P0-18, increments 1-2)

Date: 2026-10-29
Branch: `work/bug-hunt`
Refs: P0-18 (systemic), P0-16 residuals, P1-8

## Root cause, one sentence

CPython validates a builtin method's `self` with `PyLong_Check` / `PyFloat_Check`, which accept
subclass instances because the value lives in the same object; SharpPy used exact CLR type tests
(`self is PyInt`), and a CLR subclass instance is not the base CLR type - so every builtin method
and getset on a subclass of `int` / `float` raised
`descriptor 'NAME' requires a 'int' object but received a 'I'`.

## Change

* `PyInt.TryLongSelf(self, out PyInt value)` - exact `PyInt`, else `PyIntSubclass.GetIntValue()`.
  9 gates in `type/PyInt.cs` rewritten to it.
* `PyFloat.TryFloatSelf(self, out PyFloat value)` - same shape with `PyFloatSubclass.GetFloatValue()`.
  8 gates in `type/PyFloat.cs` rewritten to it.

The helpers are the CLR counterpart of the `*_Check` macros; keeping them next to the type means the
remaining builtin types (`bytes`, `str`, `list`, `dict`) get the same one-line form in later
increments instead of nine more hand-rolled tests.

## Measured

* 18-case subclass probe (`I(int) / S(str) / Bs(bytes) / L(list)`): divergences 10 -> 8 lines after
  increment 1; `bit_length` and the int getsets now match.
* 51-case float matrix: divergent lines **34 -> 22** after increment 2. Closed: `format(FSub(2.5),
  '.2f')`, `f"{x:.2f}"`, `format(x, "08.2f")`, `is_integer()`, `as_integer_ratio()`, `hex()`.
* `tools/run_bughunt.py`: 53/53 (increment 1), **54/54** (increment 2, includes the new p49 probe).
* `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}` in both increments.
* Durable probe: `tests/bughunt/p49_subclass_gates.py`, 14 lines, stdout diff **0** (the int/str/
  bytes/list cases that pass; excluded labels are named in the file header and in the ledger).

## Still open in P0-18 (measured, not inferred)

reflected operators (`1 + I(5)`, `1 + FSub(2.5)`), `round()` on builtin subclasses, `abs(FSub)`,
`"%.3f" % FSub`, `hash(FSub(2.5)) == hash(2.5)` (=> dict/set identity), `repr` of a `list` subclass,
and `format(Bs(b"ab"), ">4")` reporting `bytes.__format__` where CPython reports `Bs.__format__`.

## Recorded anomaly (no number yet)

Extending the probe with the float cases produced a file where **both** engines abort on
`FSub(10 ** 400)` (CPython prints the matching `EXC OverflowError ...` line and then its own
traceback), and with that case removed SharpPy emits an unhandled traceback on the final case
(`x.no_such`) even though the identical statement is caught and printed by
`tests/bughunt/p48_float_subclass.py`. Same statement, different outcome depending on surrounding
file state: a candidate defect in exception-state handling. No minimal reproducer yet, so it is not
numbered as a P0 and the float extension of the probe is deferred. The 54/54 pass does not cover it.
