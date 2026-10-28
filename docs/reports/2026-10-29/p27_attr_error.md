# p27 — attribute-error message parity (class vs instance vs module)

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured divergence (before)

Attribute-not-found errors on **class objects** used the metaclass type name
instead of CPython's `type object '<class>'` form:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `int.__len__` | `type object 'int' has no attribute '__len__'` | `'type' object has no attribute '__len__'` |
| `C.foo` (user class) | `type object 'C' has no attribute 'foo'` | `'type' object has no attribute 'foo'` |
| `del C.foo` | `type object 'C' has no attribute 'foo'` | `'type' object has no attribute 'foo'` |
| `C().__len__` (instance) | `'C' object has no attribute '__len__'` | `'C' object has no attribute '__len__'` (OK) |
| `(5).__iter__` (instance) | `'int' object has no attribute '__iter__'` | OK |
| `collections.nonexistent` | `module 'collections' has no attribute …` | OK |

## C vs C#/CLR reasoning

CPython has two distinct not-found paths:
- `_PyObject_GenericGetAttrWithDict` (instances):
  `'%s' object has no attribute '%U'` with the instance's type name.
- `type_getattro` (class objects):
  `type object '%.50s' has no attribute '%U'` with the **class's own** `tp_name`
  (`int`, `C`), not the metaclass name.

SharpPy used one generic `''{GetTypeName()}'' object …` message everywhere, and
`GetTypeName()` on a class object is the metaclass name `"type"`, so classes
mis-reported as `'type' object`.

## Fix

- `core/PyObject.cs`: introduce `protected virtual System.Exception
  AttributeNotFound(string name)` (default instance form) and route both the
  get (end of `GenericGetAttribute`) and the default delete not-found throws
  through it.
- `core/PyType.cs`: override `AttributeNotFound` →
  `type object '{Name}' has no attribute '{name}'`. This covers native types
  (`int`, `str`) **and** user classes, since `PyClass : PyType` and `Name` is
  the class name; `type` itself resolves via `PyTypeMetaclass.GetAttribute →
  base.GetAttribute` to the same override.
- `type/PyClass.cs`: `PyClass.PyDelAttribute` now throws the
  `type object '<class>' has no attribute '<name>'` form for a missing class
  attribute (was `'{GetTypeName()}' object`).

Instance (`'C' object …`) and module (`module 'collections' …`) messages are
unchanged and were already correct.

## Verification

- New probe `tests/bughunt/p27_attr_error.py`: stdout diff `0` vs CPython
  (class getattr, user-class getattr, instance getattr, user-class delattr,
  module getattr, dynamic-class instance getattr).
- `tools/run_bughunt.py`: **34/34 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION`; `int_loop +1.3%`, `list_comp -0.2%`,
  `dict_ops +3.6%`. The virtual is only reached on the not-found path, so
  successful attribute lookups are unaffected.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residual gaps recorded

- `del int.__len__` (and any `setattr`/`delattr` on a native/immutable type)
  should raise `TypeError: cannot set '<name>' attribute of immutable type
  'int'`. SharpPy currently raises the not-found `AttributeError`. This is the
  "immutable-type setattr/delattr" feature (CPython `type_setattro` static-type
  branch) and is deferred as its own cluster because it touches the
  class-attribute write path broadly.
- Descriptor-kind classification (`list.__len__` should be a
  `wrapper_descriptor`/`<slot wrapper …>` not a `method_descriptor`, bound form
  should be a `method-wrapper`, `len.__repr__` should bind name/type) remains
  part of the larger builtin-method TypeDict-materialization cluster.
