# P0-40: a container subclass gets the slots it inherits

Date 2026-10-30, branch `work/bug-hunt`, base `e37bf2c`.
Started from the matrix `.artifacts/tmp/submatrix.py`, grown into
`.artifacts/tmp/p40_measure*.py` and `.artifacts/tmp/p40_{dict,inplace,rest}.py`
as each layer turned out to hide the next one.

## Measured start state

`class C(list): pass` and friends, SharpPy against CPython 3.12.15:

| observable | CPython | SharpPy before |
|---|---|---|
| `repr(L([1,2]))` / `str(D({'a':1}))` | `[1, 2]` / `{'a': 1}` | `<__main__.L object at 0x…>` |
| `repr(S([1]))` / `repr(S())` | `S({1})` / `S()` | object repr |
| `C([1,2])` for set / frozenset | works | `TypeError: C() takes no arguments` |
| `len(F())` | `0` | `TypeError: object of type 'F' has no len()` |
| `s.add(1)` on a set subclass | works | `descriptor 'add' for 'set' objects doesn't apply to a 'C' object` |
| `C({1,2}) == {1,2}` (either order) | `True` | `False` |
| `bool(C())` | `False` | `True` |
| `C([1]) + [2]`, `C([1]) * 2`, `S([1]) \| F([2])`, `D({}) \| {}` | values | `unsupported operand type(s)` |
| `hash(F([1,2])) == hash(frozenset([1,2]))` | `True` | `False` |
| `x=[1]; x.append(x); repr(x)` | `[1, [...]]` | **Stack overflow, process dies** |
| `F([1]).copy()` | `frozenset({1})` | CLR `InvalidCastException` |
| `D({'a':1}).popitem()`, `.clear()` | values | `descriptor 'popitem' requires a 'dict' object` |
| `x = L([1]); y = x; x += [2]` | `L([1,2])`, `y is x` | plain `list`, alias lost |

So "list/dict subclass repr is wrong" was the visible tip: set and frozenset subclasses were not
supported at all, and three further families (comparison, truthiness/hash, operators) were missing
for every container subclass.

## The mechanism, from the pinned source

CPython copies nothing into a subclass. `PyType_Ready`'s slot inheritance
(`Objects/typeobject.c`: `inherit_slots` / `update_one_slot`) installs the *base's* C function as the
subclass slot, so `class C(list)` ends up owning `list_length`, `list_repr`, `list_subscript`,
`list_concat`, `set_or`, … and the value it reads is the object itself. Everything SharpPy does here
is the transcription of that one idea onto a runtime where the value lives in a separate storage
object.

| behaviour | anchor |
|---|---|
| list subclass repr | `Objects/listobject.c:379 list_repr` |
| dict subclass repr | `Objects/dictobject.c:2400 dict_repr` |
| set/frozenset repr, **name comes from the object** | `Objects/setobject.c:507 set_repr`: empty `"%s()"`, exact set `"{%U}"`, anything else `"%s({%U})"` with `Py_TYPE(so)->tp_name` |
| `set.__new__` ignores the iterable | `Objects/setobject.c:1038 set_new` → `make_new_set(type, NULL)` |
| `set.__init__` consumes it, and its two names differ | `Objects/setobject.c:1977 set_init`: `_PyArg_NoKeywords("set", …)` vs `PyArg_UnpackTuple(args, Py_TYPE(self)->tp_name, 0, 1, …)` |
| frozenset builds the value in tp_new and has **no tp_init** | `Objects/setobject.c:1003 frozenset_new`, spec `Objects/setobject.c:2262 "0, /* tp_init */"` |
| keyword refusal applies to subclasses too | `frozenset_new`'s condition compares `type->tp_init` against `PyFrozenSet_Type.tp_init`; both are `object_init`, so `F(x=1)` raises (measured — my first reading of the condition was wrong and the measurement corrected it) |
| constructor refusals | `object_new` (`"%.200s() takes no arguments"`), `object_init` (args accepted when `tp_init == object_init` and `tp_new != object_new`) |
| equality accepts subclasses | `list_richcompare` / `dict_equal` / `set_richcompare` test with `PyList_Check` / `PyDict_Check` / `PyAnySet_Check` |
| concatenation and merge accept subclasses | `list_concat`, `dict_or` (PEP 584), `set_or/set_and/set_sub/set_xor` |
| `bool(C())` is false | `Objects/object.c:1675 PyObject_IsTrue`: `nb_bool`, then `sq_length`/`mp_length` |
| frozenset subclass hash equals base hash | inherited `tp_hash` (contents, not identity) |
| `[1, [...]]` / `{1: {...}}` | `Py_ReprEnter` / `Py_ReprLeave` around `list_repr` / `dict_repr` |
| `x += y` keeps the object | `nb_inplace_add` = `list_inplace_concat`, `set_inplace_union`, `list_inplace_repeat`: mutate and **return self** |

## What changed

`core/PyNativeStorage.cs` (new) — the single answer to "what is this instance's value, and what does
a builtin descriptor say when there is none". It replaces the reflection that `PyList` was using to
reach `PyClassInstance.GetListStorage` (two helpers calling `MethodInfo.Invoke` on every list method
call) and it replaced the registration in `PyFrozenSet`'s static constructor, whose
`((PyFrozenSet)self).Copy()` hard cast is what made `C([1]).copy()` die with an CLR
`InvalidCastException`.

`core/PyReprGuard.cs` (new) — `Py_ReprEnter`/`Py_ReprLeave` with a thread-local identity set, used by
`PyList.ToRepr` and `PyDict.ToRepr`. Before it, a self-containing list was not a wrong string but a
dead process.

`type/PyClass.cs` (`PyClassInstance`) — set and frozenset storages next to the existing list/dict/str
ones (`make_new_set` gives an empty table before tp_init; frozenset's value materialises from the
construction arguments like `str`'s already did), plus one accessor `NativeStorage()` that every
protocol question now goes through: repr (with `set_repr`'s type-name rule, so `C({1, 2})` and
`C()`), length, containment, iteration, truthiness, comparison, hash, and the inherited binary
slots. The per-type `if (_dictStorage != null) … if (_listStorage != null) … if (strStorage …)`
chains collapsed into that one call.

`core/PyType.cs` — `__new__` for set/frozenset and `__init__` for set registered where CPython has
`tp_new`/`tp_init`; this is also what makes `TypeNewIsObjectNew` answer correctly, which was the
source of the false `C() takes no arguments`. `list.__new__` and `dict.__new__` got the same
treatment so the four container bases no longer differ from each other. The 17 set method
descriptors now resolve through `PyNativeStorage`, and the frozenset method table (`copy`, `union`,
`intersection`, `difference`, `symmetric_difference`, `isdisjoint`, `issubset`, `issuperset`) exists
at all — it had been a `// TODO: frozenset 메서드들 구현`. `TpName(PyObject)` centralises the
`tp_name` vs `GetTypeName()` distinction that had been solved locally in `tuple.__new__` and produced
`set.__new__(type): type is not a subtype of set` where CPython names `int`.

`type/PyList.cs`, `type/PyDict.cs`, `type/PySet.cs` — right operands are seen through
(`PyList_Check`/`PyDict_Check`/`PyAnySet_Check` accept subclasses), `set_repr` formatting unified so
the exact and the subclass form share one implementation, and `regListBin` grew `returnsSelf` so the
two in-place entries return the object instead of the storage. `dict.popitem` / `dict.clear` self
checks accept subclasses.

## Gates

- build: 0 errors, 0 warnings
- `tools/run_bughunt.py`: **84/84 matching** (`tests/bughunt/p79_container_subclasses.py` added,
  137 lines, diff 0: repr/str for four bases, subclass `__repr__` still winning, construction,
  methods through the MRO, truthiness/len/contains/iteration, equality in both directions,
  `+ * | & - ^`, hash-by-contents for frozenset and the three unhashable refusals, self-containing
  list and dict, declared dunders still winning (`Count.append`, `WithInit.__init__`), the
  constructor and arity refusals measured above, and the in-place operators)
- `tools/evaluate.py --timeout 90`: `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}`
- Godot 4.7.2 mono source build + recorded replay: **82 matched, 0 other**

## Performance, against an `e37bf2c` worktree, alternating rounds, best-of

```
repr_list    old 384 ms  new 385 ms  1.00x     cpython  71 ms
repr_dict    old 335 ms  new 337 ms  1.00x     cpython  50 ms
list_concat  old 427 ms  new 431 ms  1.01x     cpython  29 ms
list_eq      old 578 ms  new 579 ms  1.00x     cpython  37 ms
compute      old 949 ms  new 943 ms  0.99x     cpython  52 ms
TOTAL        old 2673 ms new 2675 ms 1.00x
```

No measured cost. The repr guard is one identity-set insert/remove per container repr, and the
operand unwrapping is a type test on a path that already did a type test.

## Remainders (registered, not fixed)

1. `class M(list, dict)` — CPython: `TypeError: multiple bases have instance lay-out conflict`;
   SharpPy accepts it. The real rule is CPython's best-base computation, which must keep
   `class E(str, int)` legal for `IntEnum`, so it needs its own commit rather than a two-type check.
2. `s.__ior__([2])` called **directly** returns `NotImplemented` in CPython; SharpPy raises
   `unsupported operand type(s) for |=: 'S' and 'list'`. Chosen deliberately: the operand test is
   shared, and the operator form (`s |= [2]`, the one real code uses) is the observable that keeps
   the `|=` symbol in its message.
3. Traceback anchor spans for these constructions still differ by caret length (`^^^^^^` vs `^`),
   which is the column-coordinate work in P0-34 cohort 3, not a slot problem.
4. `PyClassInstance` still keeps one storage field per builtin base instead of a single value slot
   with the base's type recorded. Every operation now asks `NativeStorage()`, so the remaining cost
   of that shape is bounded to the class itself; converting it is a separate change.
