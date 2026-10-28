# p28 — immutable-type setattr/delattr parity

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured divergence (before)

CPython rejects attribute writes on static (non-heap) types with a specific
`TypeError`, and reports missing-`__dict__` instance writes as an
`AttributeError`. SharpPy used a generic "read-only" `AttributeError`:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `int.foo = 1` | `TypeError: cannot set 'foo' attribute of immutable type 'int'` | `AttributeError: 'type' object attribute 'foo' is read-only` |
| `list.bar = 1` | `TypeError: cannot set 'bar' attribute of immutable type 'list'` | `AttributeError: 'type' object attribute 'bar' is read-only` |
| `del int.__len__` | `TypeError: cannot set '__len__' attribute of immutable type 'int'` | `AttributeError: type object 'int' has no attribute '__len__'` |
| `(5) x.foo = 1` (no `__dict__`) | `AttributeError: 'int' object has no attribute 'foo'` | `AttributeError: 'int' object attribute 'foo' is read-only` |
| `C.z = 2` / `del C.z` (user class) | ok | ok (already) |

## C vs C#/CLR reasoning

CPython `type_setattro` (Objects/typeobject.c) checks
`if (!(type->tp_flags & Py_TPFLAGS_HEAPTYPE))` **before** any descriptor lookup
and raises
`TypeError("cannot set '%U' attribute of immutable type '%s'")`. Static builtin
types (`int`, `list`, …) are non-heap; user classes are heap types and stay
writable. Separately, `_PyObject_GenericSetAttrWithDict` on an instance that has
neither a matching data descriptor nor a `__dict__` raises
`AttributeError("'<T>' object has no attribute '<name>'")` — not a "read-only"
message.

SharpPy models native builtins as `PyType` instances that are **not** `PyClass`
(the heap-class type). That distinction is the CLR analogue of the heap/non-heap
flag.

## Fix (`core/PyObject.cs`)

- `PySetAttribute` and `PyDelAttribute`: if `this` is a `PyType` that is not a
  `PyClass` (a native/non-heap type), throw
  `TypeError: cannot set '<name>' attribute of immutable type '<Type.Name>'`
  before descriptor lookup — matching `type_setattro`.
- The generic instance fall-through (no descriptor, no `__dict__`) now raises
  `AttributeError: '<T>' object has no attribute '<name>'` instead of the
  "read-only" wording.

User (`PyClass`) writes/deletes are unaffected; type creation writes to
`ClassDict`/`TypeDict` directly, not through `SetAttr`, so internal
initialization is unaffected (confirmed by the full suite).

## Verification

- New probe `tests/bughunt/p28_immutable_type.py`: stdout diff `0` vs CPython
  (native-type set, native-type dunder delete, `__module__` set, list set,
  no-`__dict__` instance set/del, user-class set/del/new/read-back).
- `tools/run_bughunt.py`: **35/35 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION`; `int_loop +1.5%`, `list_comp +0.9%`,
  `dict_ops +2.2%`. The added guard is a single CLR type-check on the `obj.x=`
  path, not in the local-store/loop hot paths.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residual gaps recorded

- `type` is modelled as a heap `PyClass` in SharpPy, so `type.foo = 1` is still
  permitted there, whereas CPython's static `PyType_Type` raises the immutable
  `TypeError`. This is part of the broader "make `type`/`super` globals faithful
  static types" item, not addressed here to avoid destabilizing the class
  construction path that relies on `PyTypeMetaclass` being writable.
