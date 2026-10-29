# p34 — dir()/getattr materialization: list & tuple structural dunders

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured `dir()` gaps (all MISSING, no EXTRA — p22 logic is clean)

| type | missing dir() names |
|---|---|
| list | `__reversed__` |
| tuple | `__contains__`, `__getnewargs__`, `__iter__` |
| dict | `__contains__`, `__iter__`, `__len__`, `__reversed__` |
| int/bool | 12 (`__index__`, `__round__`, `is_integer`, `__rpow__`, …) |
| str | 5 · float | 28 · set | 15 · bytes | 54 |

These are CPython `tp_dict` slot wrappers / method descriptors. In SharpPy the
behaviours exist as CLR virtuals but are neither in `TypeDict` (so `dir()` omits
them) **nor** gettable (`hasattr(int, '__index__')` was `False`). CPython has
both, so a "dir-only name list" would be untruthful — the fix must add real,
gettable, callable descriptors (the pattern `PyList.cs` already uses for
`__getitem__`/`__iter__`/…).

## Implemented this cluster — list + tuple (exact)

- **`list.__reversed__`** (`PyMethodDescriptor`): `new PyReversedIterator(list,
  n, "list_reverseiterator")` via `GetListStorage` (so list subclasses work).
  `dir(list)` now matches CPython exactly.
- **`tuple.__iter__`, `tuple.__contains__`** (`PyWrapperDescriptor` slots) and
  **`tuple.__getnewargs__`** (`PyMethodDescriptor`). `dir(tuple)` matches exactly.
  The delegates use a **strict `is PyTuple` guard + typed call**, not the
  `PyObject` virtual.

Descriptor kinds and reprs match CPython: `<method '__reversed__' of 'list'
objects>`, `<slot wrapper '__iter__' of 'tuple' objects>`, etc.

## Critical hazard found (why dict is deferred, not "fixed")

Adding a VM-special dunder (`__iter__`/`__contains__`/`__len__`/`__reversed__`) to
a builtin's `TypeDict` makes the VM's **special-method lookup** pick it up for
subclasses too. `collections.OrderedDict` is modelled as a user
`class OrderedDict(dict)` = a `PyClassInstance` with **no CLR `PyDict` storage**:

- `self.GetIterator()` (PyObject virtual) re-enters the `__iter__` descriptor →
  **infinite recursion → stack-buffer-overrun crash** (exit `0xC0000409`), which
  broke `p15_modules_stdlib.py`.
- A strict `is PyDict` guard avoids the crash but raises wrong-self on the
  subclass, also breaking p15.

`list` avoids this because `GetListStorage()` resolves a subclass to its backing
CLR `PyList`. **There is no `GetDictStorage()`**, so dict structural dunders
cannot be added safely yet; they were reverted with an explanatory comment.
Tuple uses a strict typed guard (real tuples are exact; tuple subclasses would
error rather than recurse — untested, so no crash).

## Verification

- `tests/bughunt/p34_dir_materialize.py`: stdout diff `0` (dir(list)/dir(tuple)
  full lists, `hasattr`, explicit `__reversed__`/`__iter__`/`__contains__`/
  `__getnewargs__`, descriptor kinds + reprs, syntax-path non-regression).
- `tools/run_bughunt.py`: **40/40 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION` (change is descriptor registration, not
  a hot loop; syntax dispatch unchanged).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Deferred (follow-ups, each with a concrete prerequisite)

1. **dict structural dunders** — add a `PyClassInstance.GetDictStorage()`
   resolver (mirroring `GetListStorage`) so subclass iteration stays non-recursive,
   then register `__iter__`/`__len__`/`__contains__`/`__reversed__`.
2. **int / bool / float / set / str / bytes** — the missing set is dominated by
   arithmetic / reflected / named-method dunders. Materializing them requires a
   public, frame-free **binary/number dispatch bridge** (the VM's
   `ExecuteBinaryOpType` is a private instance method today). Structural-only
   entries (e.g. `float.__getnewargs__`) could land without it.
3. **repr of legacy `object` wrapper descriptors** — `object.__eq__` still shows
   `<wrapper_descriptor object at 0x…>` instead of `<slot wrapper '__eq__' of
   'object' objects>`; the new descriptors use the correct form.
