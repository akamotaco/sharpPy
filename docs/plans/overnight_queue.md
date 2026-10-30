# Overnight queue (2026-10-31 03:20 → 07:20)

Owns: continue P0/P1 work without the user. Each queue entry is one commit: measure the failing
observable first, read the CPython 3.12.15 anchor, implement one mechanism, then run the gates.

Fixed paths and commands

- repo `C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy`, branch `work/bug-hunt`, HEAD `58dbabb`
- build `C:/Users/AMD-MAX395-128G-2T/AppData/Local/SharpPy/dotnet/dotnet.exe build -c Release -p:SkipCodeGeneration=true`
- run `.../dotnet.exe bin/Release/net8.0/sharppy.dll <script.py>`
- reference `C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe` (PATH python is 3.14 and lies)
- gates: `tools/run_bughunt.py` (currently **85/85**),
  `tools/evaluate.py --dotnet <dotnet> --timeout 90` (currently `225/3/2`),
  `tools/cpython_similarity.py` (exit code = broken C anchors, currently 0),
  Godot replay when type/VM/IO/lifetime/class-creation semantics change
  (`.artifacts/round30-godot`, Release build, copy Release dll into Debug, `-- --replay`, expect
  **82 matched, 0 other**), perf A/B against a `git worktree` of the base commit
- shell: `export PYTHONIOENCODING=utf-8`, one action per command, no nested heredocs,
  write inline python into `.artifacts/tmp/*.py`

## Q1 — P0-43b step 1: make the wrapper choice one place (no behaviour change yet)

Today each builtin decides its own subclass wrapper at its own construction site:
`core/PyType.cs:1015` (str), `core/PyType.cs:2864` (tuple), `type/PyBytesSubclass.cs:43`,
`type/PyString.cs:1662`, `type/PyInt.cs`. Add one factory
`PySubclassFactory.Create(PyClass cls, PyObject[] args)` that reads the type flags
(`PyTypeFlags.ListSubclass / DictSubclass / TupleSubclass / BytesSubclass / UnicodeSubclass /
LongSubclass`, `core/PyTypeFlags.cs`) and returns the right shape, then route those five sites
through it. No new behaviour; the point is that a later step changes one place.

Gate: bughunt 85/85, evaluate unchanged. Perf: `list_comp`, `str_pct`, `str_fmt` via A/B.

## Q2 — P0-43b step 2: containers become CLR subtypes

`PyListSubclass : PyList`, `PyDictSubclass : PyDict`, `PySetSubclass : PySet`,
`PyFrozenSetSubclass : PyFrozenSet`, each carrying `InstanceType` + `InstanceDict` the way
`PyTupleSubclass` (`type/PyClass.cs:2648`) and `SubclassInstanceSupport` already do.

Why this is the faithful shape: in CPython a list subclass instance **is** a `PyListObject` whose
`ob_type` is the subtype; `PyList_Check` reads the flag and the value is the object
(`(PyListObject *)op`). With CLR inheritance `x is PyList` *is* that cast, so the storage indirection
disappears instead of being threaded through every operation.

Then delete: `_listStorage/_dictStorage/_setStorage/_frozenStorage` and their getters,
`PyNativeStorage.ForList/ForDict/ForSet/ForFrozenSet` collapse to casts, `Unwrap` mostly dies.
Expect `storage_api_references` (126) and `builtin_type_self_checks` (34) to fall - record
before/after with `tools/cpython_similarity.py --json .artifacts/similarity/{before,after}_p043b.json`.

Watch out: 68 sites test/cast `PyClassInstance` in 19 files (`core/PyBuiltin.cs:3776`,
`core/PyType.cs:2882` are the ones that use it to mean "a user instance"); each one must be re-read,
not bulk-replaced. `__dict__` access, `setattr`, `super()` and pickling paths are the risky ones.

## Q3 — P0-44: class statement with a non-type base

Measured in `.artifacts/tmp/p43_nonbase2.py`; SharpPy currently creates a class for all four.
Mechanism is *calling* the computed metaclass, which is a different path from `type('C',(b,),{})`:

| base | CPython 3.12.15 |
|---|---|
| `5` | `TypeError: int() takes at most 2 arguments (3 given)` |
| instance of `Inst` | `TypeError: Inst() takes no arguments` |
| `len` | `TypeError: cannot create 'builtin_function_or_method' instances` (that type has no `tp_new`) |
| `None` | `TypeError: NoneType takes no arguments` |

Anchor: `_PyCalcMeta` picks `type(bases[0])`, then `__build_class__` calls it with
`(name, bases, ns)`. Add probe `tests/bughunt/p81_non_type_base_statement.py`.

## Q4 — P0-45: real surface holes found by the similarity tool

`set.__reduce__`, `frozenset.__reduce__` (pickle), `set.__sizeof__`, `frozenset.__sizeof__`,
`dict.__sizeof__`, `str.__getnewargs__`. Measure each with the reference interpreter before writing
anything: `.__sizeof__()` returns an int, `__reduce__()` returns a tuple whose first element is the
reconstructing callable - pin the shape, not the byte count, and mark int values volatile if the
allocator leaks into them. Anchors: `Objects/setobject.c` `set_reduce`/`set_sizeof`,
`Objects/dictobject.c` `dict_sizeof`, `Objects/unicodeobject.c` `unicode_reduce`.

## Q5 — P0-46: registration locality (start with set/frozenset)

`type/PySet.cs` registers **0** of the set surface; all 53 entries sit in `core/PyType.cs`. Move
set/frozenset method registration and their `PyTypeObject`-shaped spec (flags, methods, dunders) into
`type/PySet.cs`, mirroring `Objects/setobject.c` keeping `set_methods` next to `PySet_Type`. Verify
the CLR static-constructor order rule still holds (`PyType` static ctor forces builtin type dicts in
dependency order) - breaking it is P0-14's territory, guarded by `tests/bughunt/p65_registration_order.py`.
Then `tools/cpython_similarity.py`'s own-file column should stop reading 0.

## Q6 — morning wrap (before any push of a half-step)

1. full gates: build, `run_bughunt.py`, `evaluate.py --timeout 90`, `argv_matrix.py`, Godot replay,
   `cpython_similarity.py`
2. perf A/B for whatever was touched, against the commit the queue started from
3. `docs/known_gaps.md`: close what closed, keep remainders explicit, update the snapshot numbers
4. commit + push; summary for the user: what changed, numbers before/after, what is still open and why

Do not open new product areas at 07:00. If a step is stuck after two honest attempts, write the
measured state into `docs/known_gaps.md`, leave the tree green, and move to the next entry.
