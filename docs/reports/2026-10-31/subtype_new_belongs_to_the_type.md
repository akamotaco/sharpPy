# P0-43b Q1: a subtype instance is built by the type it derives from

Date 2026-10-31, branch `work/bug-hunt`, base `6f97681`. First entry of
[docs/plans/overnight_queue.md](../plans/overnight_queue.md).

The queue said "add one factory for subclass wrappers". Reading CPython first changed that: CPython
has **no** central factory. `type_call` calls `tp_new`, and each type's `tp_new` knows how to build a
subtype of itself - `float_new` → `float_subtype_new` (Objects/floatobject.c:1677),
`unicode_subtype_new`, `long_subtype_new`, `bytes_new` → `bytes_subtype_new`, `tuple_new`. A central
factory would have been a new abstraction of mine, not a translation. What the tree actually had was
the same rule written in several places, some of them contradicting the type they imitated.

## The defect the missing slot caused

`float` had no `__new__` at all (`Objects/floatobject.c:1644` is the clinic header for it). Two
consequences, both measured:

1. a user float subclass could not forward to its base:

   ```python
   class D(float):
       def __new__(cls, v=0.0):
           return super().__new__(cls, v * 2)
   D(2.0)
   ```

   CPython → `4.0`. SharpPy → `TypeError: object.__new__() takes exactly one argument
   (the type to instantiate)`, because `super().__new__` had nothing to resolve to except
   `object.__new__`.

2. the keyword message was invented rather than CPython's: `float.__new__() takes exactly one
   argument (the type to instantiate)` where CPython says `float() takes no keyword arguments`
   (`_PyArg_CheckPositional("float", nargs, 0, 1)`, Objects/floatobject.c:1705).

## What changed

`type/PyFloat.cs` gained `FloatNew` - the single implementation of `float_new`/`float_subtype_new` -
and the `float.__new__` slot that calls it. Exact type converts (`float_new_impl`, x defaults to 0 at
:1655); a subtype converts first and then gets the value copied in, which is why a conversion failure
reports the same error for `C('z')` as for `float('z')`.

Deleted from generic code, because the slot now owns the rule:

- the float family branch in `PyClass.CreateInstanceCore` (it was reached only when `__new__` was
  builtin-ish, i.e. exactly when the slot could have answered)
- the bytes-subclass branch in `PyType.Call` and the bytes branch in `PyClass.CreateInstanceCore`:
  `bytes.__new__` (`type/PyBytes.cs:210`) already honours the requested type through
  `bytes_subtype_new`, so three copies existed where CPython has one
- the float branch in `PyType.Call` now forwards to `FloatNew` instead of repeating the conversion,
  the arity refusal and the keyword refusal; CPython likewise reaches one parser from
  `float_vectorcall` (Objects/floatobject.c:1698)

`tools/cpython_similarity.py` gained the metric that sees this: *family branches inside generic
paths* counts `DerivesFromType(` inside `core/PyType.cs`, `type/PyClass.cs` and `runtime/PyVM.cs`,
where CPython keeps nothing of the sort.

```
family branches inside generic paths   7 -> 3
*_subtype_new call sites               7 -> 3
```

The last two came from deleting, not from hiding: `PyType.Call`'s float-subclass branch turned out to
be **dead** once the slot existed (removing it left bughunt at 86/86 and the float probe
line-for-line identical to CPython), and `PyFloatSubclass.FloatSubtypeNew` - whose own doc comment
claimed it was the "single implementation shared by PyClass.CreateInstance and PyType.CreateInstance" -
lost its last caller at that point and went with it.

## Gates

- build: 0 errors, 0 warnings
- `tools/run_bughunt.py`: **86/86 matching** — `tests/bughunt/p81_float_new_and_super.py` added
  (diff 0): the float matrix (`C(1.5)`, `C('2.5')`, `C(3)`, `C(b'1.25')`, `C()`, attribute set,
  `float(c)`, `c.real`), `super().__new__` on **all five** payload bases (float, int, str, tuple,
  bytes), and the three refusals (`float() takes no keyword arguments`,
  `float expected at most 1 argument, got 2`, `ValueError: could not convert string to float: 'z'`)
- `tools/evaluate.py --timeout 90`: `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}`
- Godot 4.7.2 mono source build + recorded replay: **82 matched, 0 other**
- perf A/B against a `6f97681` worktree build, alternating runs, best of 3:
  `float_text 1.02x, float_number 0.99x, bytes_copy 0.99x, str_call 0.99x, int_call 1.01x,
  subclass_float 1.03x, subclass_bytes 0.95x` — noise, no measured cost

## Left open by this entry

- `float.__new__` reprs as `<builtin_function_or_method object at 0x…>`; CPython says
  `<built-in method __new__ of type object at 0x…>`. That is the existing builtin-repr family, not
  this slot, so it stays out of the pinned probe.
- Remaining family branches: `str` in `PyType.Call` (`core/PyType.cs:1004`, `:1010`) and the
  bytes-like `encoding without a string argument` test at `:975`. Those two str sites are the next
  thing to move into `str.__new__`, which already exists (`type/PyString.cs:1625`).
- Containers still build their instances through `PyClassInstance` storages rather than through
  `list.__new__`/`dict.__new__`/`set.__new__`/`frozenset.__new__` returning a CLR subtype — that is
  Q2, and this entry is what makes Q2 a shape change instead of a behaviour change.
