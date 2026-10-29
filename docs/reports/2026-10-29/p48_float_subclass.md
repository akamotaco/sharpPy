# p48 — float subclasses carry a value (P0-16, partial)

Date: 2026-10-29
Branch: `work/bug-hunt`
Status: **partial**. Value semantics restored; 34 of 51 measured cases agree. Residuals registered.

## CPython shape

`Objects/floatobject.c`:

```c
float_new(PyTypeObject *type, ...) {
    if (type == &PyFloat_Type) { ...plain float... }
    return float_subtype_new(type, args, kwds);
}
float_subtype_new(type, args, kwds) {
    tmp = PyNumber_Float(...);                     /* convert first */
    newobj = type->tp_alloc(type, 0);
    ((PyFloatObject *)newobj)->ob_fval = ((PyFloatObject *)tmp)->ob_fval;  /* then copy value */
}
```

So a float subclass instance *is* a float object with a different `ob_type`. SharpPy had this shape
for `int` (`PyIntSubclass`) and `str` (`PyStrSubclass`) but not `float`: `class FSub(float)` built an
ordinary object with no value at all - `repr(FSub(2.5))` was `<__main__.FSub object at 0x...>`,
`FSub(2.5) + 1` raised `TypeError`, `FSub(2.5) == 2.5` was `False`, `float(FSub(2.5))` raised.

## What landed

* `type/PyFloatSubclass.cs`: wraps a `PyFloat` payload plus the `PyClass`, implements
  `IInstanceDictAccessor`, and delegates arithmetic / comparison / hash / conversions / `str` /
  `repr` to the payload while `GetPyType()`/`GetTypeName()` report the subclass. `AsFloat()` returns
  an exact `float`.
* `PyFloatSubclass.FloatSubtypeNew(cls, args, kwargs)` holds the semantics once, called from both
  `PyClass.CreateInstance` (only when the MRO's `__new__` is a builtin form, so a user `__new__`
  still wins) and `PyType.CreateInstance`.

Fixed and measured: `repr`, `str`, `+ - * / // % **` (left side), comparisons, `bool`, `int()`,
`float()`, `type(x).__name__`, `isinstance(x, float)`, instance `__dict__`, `x.k` attribute,
`[x, x]` repr, and a float-subclass method calling `self * 2`.

## Two structural findings

1. **`PyClass.CreateInstance` is `public new`, hiding `PyType.CreateInstance` instead of overriding
   it.** The receiver's *static type* decides which implementation runs. My first attempt - a branch
   in `PyType.CreateInstance`, where the p44 `float()` interception lives - never fired for
   `FSub(2.5)`. This is why both entry points now share one helper, and it is the reason the arity
   guard below is still dead: those calls take a third route I have not located yet.
2. **`float`'s own slots are not usable through the MRO.** `float.__new__` resolves to something
   that answers `type.__new__() takes exactly 4 arguments (metaclass, name, bases, dict)` when
   called as `float.__new__(cls, v)`, and `round(FSub(2.5))` says `type FSub doesn't define
   __round__ method`.

## Registered residuals (measured, not guessed)

* reflected ops / `abs`: `1 + FSub(2.5)` -> `unsupported operand type(s) for +: 'int' and 'FSub'`;
  `abs(FSub(2.5))` -> `bad operand type for abs(): 'FSub'`. The numeric fast paths know
  `PyIntSubclass`, not `PyFloatSubclass`.
* float's descriptors refuse subclass instances: `format(x, '.2f')`, `f"{x:.2f}"`, `"%.3f" % x`,
  `x.is_integer()`, `x.as_integer_ratio()`, `x.hex()` raise
  `descriptor 'NAME' requires a 'float' object but received a 'FSub'`; CPython's `PyFloat_Check`
  accepts subclasses. Same bug family as `%b` on bytes subclasses.
* arity/kwargs not enforced on the subclass path: CPython `FSub(x=1)` ->
  `float() takes no keyword arguments`, `FSub(1, 2)` -> `float expected at most 1 argument, got 2`;
  SharpPy accepts both and answers `1.0`.
* `hash(FSub(2.5)) == hash(2.5)` is `False`, so `{2.5: 'v'}[FSub(2.5)]` raises `KeyError` and
  `len({FSub(2.5), 2.5})` is 2 - tied to `__hash__` not resolving through `float`'s MRO (the P1-8
  non-materialized-slot family), so the subclass type cannot fix it alone.

## Verification

* `tests/bughunt/p48_float_subclass.py`: 34 lines, stdout diff **0**. The excluded labels are listed
  in the probe header and in the ledger, so the gap stays visible instead of being masked.
* `tools/run_bughunt.py`: **53/53 matching**.
* `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
* Godot replay not re-run; no core/type/attribute/VM semantic change beyond the new type and the
  two narrow construction branches. Re-run it if the dispatch-route work touches `PyType.Call`.
