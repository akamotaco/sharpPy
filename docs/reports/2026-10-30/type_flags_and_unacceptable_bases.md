# P0-43: tp_flags exists now, and a base that is not a type stops leaking a CLR cast

Date 2026-10-30, branch `work/bug-hunt`, base `6d559f7`.

## Why this commit exists

The question asked was structural: *how close is the implementation to CPython's own shape, and
where did it take a detour?* Answering it required two things that did not exist: a measurement, and
the layer CPython answers every "what kind of object is this" question from.

The honest starting answer, from reading the code: one Python concept ("an instance of a user class
that derives from a builtin base") had **four** representations in the tree.

| shape | CPython counterpart | users |
|---|---|---|
| `PyTupleSubclass : PyTuple`, `PyBytesSubclass : PyBytes` (`type/PyClass.cs:2648`, `type/PyBytesSubclass.cs:13`) | exact: base layout, `ob_type` replaced by the subtype, slots inherited through the vtable | tuple, bytes |
| `PyIntSubclass : PyObject`, `PyStrSubclass`, `PyFloatSubclass` | value carried inside, so every operation has to look through | int, str, float |
| `PyClassInstance` + `_listStorage/_dictStorage/_setStorage/_frozenStorage` | not in CPython at all: an extra object | object subclasses (correct) and the four containers (P0-40's choice) |
| `PyObject._customType` (`core/PyObject.cs:33-49`) | `ob_type` override — exactly the right idea, used partially | mixed |

Nothing is `sealed`, so C# single inheritance never forced the two-object shape; and CPython agrees
with single inheritance anyway, since it refuses a second variable-size base ("multiple bases have
instance lay-out conflict"). The gaps that *are* real are: no layout cast (`(PyListObject *)op`), no
slot table (SharpPy re-scans the MRO by name per operation), and **no `tp_flags` at all** — only a
comment at `core/PyType.cs:432` claiming `TypeKind` was the equivalent.

## The mechanism, from the pinned source

| fact | anchor |
|---|---|
| `PyList_Check(op)` is a feature test on `ob_type`, not a type test | `Include/listobject.h:24` |
| the test is two instructions | `Include/object.h:959` (`PyType_HasFeature`), internal spelling used at `Objects/typeobject.c:2789` |
| bit values | `Include/object.h:474,484,486,494,497,500,518,540,543,545-551` |
| a subtype gets the family bit from its best base | `Objects/typeobject.c:6726 inherit_special`, chain at 6755-6780 |
| a base without `BASETYPE` is refused before `best_base`/MRO work | `Objects/typeobject.c:2789` → `type '%.100s' is not an acceptable base type` |
| heap types copy the base's flags | measured: `class C(list)` carries `LIST_SUBCLASS` **and** `SEQUENCE` |
| static specs do *not* copy BaseType | measured: `PyBool_Type` has none although `int` does (`Objects/boolobject.c:190`) |
| `SEQUENCE`/`MAPPING` exist for pattern matching only | `Python/bytecodes.c:2252`, exclusivity asserted at `Objects/typeobject.c:7001,7021` |
| set and frozenset have **no** family bit | `Include/setobject.h:36 PySet_Check`, `:30 PyAnySet_Check` — plain `PyType_IsSubtype` |
| a base that is not a type drags its own type in as the metaclass, and the winner must be a subclass of `type` | `Objects/typeobject.c:3083` |

## Measured defects fixed

1. `class C(bool)` — CPython: `TypeError: type 'bool' is not an acceptable base type`; SharpPy created
   the class. Same for `class C(type(None))` (`NoneType`) and `class C(slice)`; `range` and
   `memoryview` are also BASETYPE-less in CPython (`Objects/rangeobject.c:785`,
   `Objects/memoryobject.c:3390`) but SharpPy's global names for those two are factories rather than
   types, so they arrive at the metaclass message instead — that is the registered
   factory-vs-type gap, not a new one.
2. `type('C', (5,), {})` — CPython: `TypeError: metaclass conflict: ...`. SharpPy: an uncaught CLR
   `InvalidCastException: Unable to cast object of type 'SharpPy.PyInt' to type 'SharpPy.PyType'`
   from `core/PyTypeMetaclass.cs:519`, printed as a `RuntimeError` with a C# stack through
   `PyVM.ExecuteCall`. Measured for four kinds of base (int, instance, `len`, `None`) CPython gives
   the same conflict message every time, which is why the check is uniform:

   ```
   int-instance call -> TypeError: metaclass conflict: the metaclass of a derived class must be ...
   plain-instance call -> same        builtin-func call -> same        none call -> same
   ```

## What landed

`core/PyTypeFlags.cs` — the bits with CPython's own values and a header anchor per line; unused CPython
bits are omitted rather than renumbered, so one can be added later without disturbing its neighbours.

`core/PyType.cs` — `Flags`, `HasFeature` (a feature test is `!= 0`, not `== flag`), `ComputeFlags()`
called where CPython computes flags (after `check_duplicates`, before MRO work), two propagation masks
because CPython has two propagation rules:

* heap type → copies the base's flags (measured: `LIST_SUBCLASS` + `SEQUENCE` show up on `class C(list)`)
* static spec → only what `inherit_special` propagates: family bits, `MATCH_SELF`, `ITEMS_AT_END`,
  and **not** `BASETYPE`

plus `BuiltInSpecFlags`, one row per compiled spec with its line, and the `BASETYPE` gate.
`type/PyClass.cs` — `IsListSubclass`/`IsDictSubclass`/`IsStrSubclass` are now flag reads (five
per-call MRO walks gone, and multi-level inheritance stops needing a comment because the bit is
propagated once at type creation), while `IsSetSubclass`/`IsFrozenSetSubclass` keep the MRO walk
because CPython keeps `PyType_IsSubtype` for those two. `core/PyNativeStorage.cs` — list/dict
resolution asks the flag first and only then performs the cast, which is the order CPython uses.

## Similarity measurement

`tools/cpython_similarity.py` (new) compares shape, not behaviour; behaviour stays with
`run_bughunt.py` and `evaluate.py`. It answers four questions:

* **M1 anchors** — every mechanism table row is re-read from the pinned tree: does the named
  function still live on that line, and does the SharpPy file cite it? 17 rows;
  **17/17 C anchors resolve, 0 rows without a SharpPy citation** after this commit (before: the flags
  row pointed at a file that did not exist).
* **M2 representations** — how many CLR shapes carry one Python concept, plus per-site CLR
  self-checks (34), storage API references (124 → 126) and reflection calls (10).
* **M3 surface** — parses each CPython type spec *positionally, by its `/* tp_repr */` comments*
  (3.12 does not use designated initialisers), takes the filled slots and the `tp_methods` table, and
  compares with what SharpPy registers:

  ```
  list      methods   2/  2 (list_methods)       slots 6/7   own-file  22  central 53
  dict      methods   8/  9 (mapp_methods)       slots 6/7   own-file  21  central 53
  set       methods  19/ 22 (set_methods)        slots 5/7   own-file   0  central 53
  frozenset methods  10/ 12 (frozenset_methods)  slots 4/6   own-file   0  central 53
  tuple     methods   1/  1 (tuple_methods)      slots 5/6   own-file  11  central 53
  str       methods   9/ 10 (unicode_methods)    slots 5/7   own-file  56  central 53
  ```

* **M4 detours** — TODO markers (70), hand-maintained capability tables (1), type-name string
  comparisons (3).

Read honestly, this commit moves M1 and fixes behaviour; **M2/M4 barely move (+2 storage
references)**, because adding the layer CPython has is not the same as deleting the shape SharpPy
should not have. That deletion is P0-43b below. The M3 numbers also expose two things I had not
measured: set and frozenset register **nothing** in `type/PySet.cs` (all 53 entries sit in
`core/PyType.cs`, where CPython keeps `set_methods` in `Objects/setobject.c`), and real surface gaps
exist at `set.__reduce__`, `frozenset.__reduce__`, `__sizeof__`, `str.__getnewargs__`.

Two stated limitations, so the numbers are not over-read: `__eq__`/`__iter__` count as missing
because SharpPy implements them as CLR virtuals (`RichCompare`, `GetIterator`) rather than `TypeDict`
entries — that is the slot-table gap, not an absent operation; and `set.test_c_api` is CPython's own
test hook.

## Gates

- build: 0 errors, 0 warnings
- `tools/run_bughunt.py`: **85/85 matching** — `tests/bughunt/p80_type_flags.py` added (89 lines,
  diff 0): the three refusals, the four non-type-base conflicts, three-level `list` subclass
  (len/index/slice/contains/eq/repr/bool/iteration/`append`/`+=`), two-level `dict` and `set`
  subclasses with operators, `frozenset` subclass hash equality, and metaclasses still working
- `tools/evaluate.py --timeout 90`: `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}`
- Godot 4.7.2 mono source build + recorded replay: **82 matched, 0 other**
- `tools/cpython_similarity.py`: exit code is the number of broken C anchors → **0**

## Next, in the order that keeps one mechanism per operation

1. **P0-43b** — replace the container subclass storage with the CLR-inheriting wrappers that already
   exist for tuple/bytes (`PyListSubclass : PyList` etc.), so `x is PyList` *is* `PyList_Check`,
   `PyNativeStorage` shrinks to the cast, and the 34 per-site self-checks and 126 storage references
   start falling. `PyClassInstance` then keeps exactly the job CPython gives it: a heap type with a
   `__dict__`.
2. **P0-44** — the class-statement path with a non-type base. Measured, and deliberately *not* fixed
   here because it is a different mechanism (calling the computed metaclass) than the one fixed above:
   CPython raises `int() takes at most 2 arguments (3 given)` / `Inst() takes no arguments` /
   `cannot create 'builtin_function_or_method' instances` / `NoneType takes no arguments`, SharpPy
   currently creates the class.
3. **P0-45** — the M3 surface gaps above (`__reduce__` for set/frozenset is a pickle hole).
4. **P0-46** — registration locality: move each builtin's surface into its own file so a comparison
   with CPython's per-type spec files lines up, and the tool's "own-file" column stops reading 0.
5. **weakref** sits on top of this: CPython's weakref support *is* a type feature
   (`Py_TPFLAGS_MANAGED_WEAKREF`, `Include/object.h:474`, declared in `core/PyTypeFlags.cs` here), so
   the module no longer needs its own list of which types may be weak-referenced.
