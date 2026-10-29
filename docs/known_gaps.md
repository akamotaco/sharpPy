# SharpPy — Known CPython-parity gaps (consolidated ledger)

Maintained in one place. Per-cluster findings live in `docs/reports/<date>/pNN_*.md`;
this file is the live backlog only. Add an entry when a probe proves a divergence,
remove it (or move to a "fixed" note) when a cluster closes it.

Reference interpreter: **CPython 3.12.15**
(`C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe`).

## Verification snapshot (this state)

- `tools/evaluate.py`: `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- `tools/run_bughunt.py`: **40/40 matching**
- Godot 4.7.2 mono replay (Release DLL): **82 matched, 0 other**
- Bytecode proximity: ~0.845; all 129 standard opcode names/numbers match.

Reading this: **execution fidelity is high** (arithmetic, containers, control
flow, closures, exceptions, generators, classes/descriptors, ABC, iterator
protocol, file I/O, regex all pass parity probes). Most open items are
`P1` *introspection/display* (dir / getattr / repr / type-name of builtin
dunders) where the **implicit operation itself already works**. `P0` items are
genuine computation/result divergences and are worked first.

---

## P0 — actual computation / behavior differs (fix first)

| # | area | divergence (measured vs CPython) | source |
|---|---|---|---|
| P0-2 | `re` backref | `re.findall(r"(?P<c>\w) (?P=c)", …)` → `[]`; CPython → `['a','c']` (named `(?P=name)`). Plain numeric `\1` works | p31 |
| P0-3 | bytes/bytearray argcheck | `b"…".find(b"c", "x")` / `count` / `index` silently ignore a non-integer start/stop; CPython → `TypeError: slice indices must be integers or None or have an __index__ method` (bool/None must still work) | p32 note |
| P0-12 | metaclass `__str__`/`__repr__` | `class M(type): __str__` is ignored for class objects: `str(C)` → `<class '__main__.C'>`, CPython → `'META'`. `PyClass.AsString/ToRepr` never consults the metaclass | p39 finding |

| P0-14 | bytearray surface | 33 methods still missing on `bytearray`: `capitalize center decode expandtabs fromhex hex isalnum isalpha isascii isdigit islower isspace istitle isupper join ljust lower lstrip maketrans partition replace rjust rpartition rsplit rstrip split splitlines strip swapcase title upper zfill` (CPython has all; only `isprintable` legitimately absent) | p40 measured |
| P0-4 | `memoryview.__eq__` | `memoryview(b"abc") == memoryview(b"abc")` and `== b"abc"` → `False`; CPython → `True` | p33 |
| P0-5 | static `type` | `type.foo = 1` succeeds; CPython → `TypeError: cannot set 'foo' attribute of immutable type 'type'` (SharpPy `type` is a heap `PyClass`) | p28 |
| P0-6 | `memoryview.cast` endian | `>`/`!` (big-endian) prefix accepted but not byte-swapped → wrong element values (rare; host is LE) | p33 |
| P0-7 | N-D memoryview | `cast(fmt, shape)` with ndim > 1 not modelled (flat view only) | p33 |
| P0-8 | `io.TextIOWrapper(buffer, encoding=…)` | wrapping an existing stream with an encoding not supported | io follow-up |
| P0-9 | importlib semantics | custom loader/spec beyond `__file__`; namespace-package behavior | p15/p18 follow-up |
| P0-10 | numeric-coercion dunders | `int(C())`/`float(C())` with `__int__`/`__index__`/`__float__` raise `TypeError: ... not 'C'`; CPython calls the dunder (`int()` = `__index__` then `__int__`, `float()` = `__float__`). `AsInt()` is a CLR cast, not dunder dispatch. `__index__` dispatch already exists via `PyObject.TryGetIndex` | p36 finding |

> API convention (enforced going forward): `To~()` → bounded **C#** value
> (`ToInt`→int, `ToFloat`→double, `ToBool`→bool); `As~()` → **Python** type
> (`AsInt`→`PyInt` arbitrary-precision, `AsFloat`→`PyFloat`, `ToStr`→`PyString`).
> `As~` must never be bounded by a `To~` cast. Naming outlier `AsString()` (C# string
> under an `As` prefix) renamed to `ToClrString()` (p37). `ToStr()` (PyString under a
> `To` prefix) is a **non-virtual alias** for `AsString()` (p39) — subclasses override
> `AsString()` only, so no double dispatch path exists. `ToString()` is debug-identity
> only. Note: in commits before p39, `AsString()` meant the C# `string` accessor.

## P1 — introspection / display differs (implicit op already correct)

| # | area | divergence | source |
|---|---|---|---|
| P1-1 | builtin dunder materialization | `dir(int/bool/float/set/str/bytes)` missing their concrete dunders/methods, and they are not `getattr`-able (`hasattr(int,'__index__')` False). Implicit `+`,`//`,`round`,etc. all work. **Prereq: a public, frame-free binary/number dispatch bridge** (`PyVM.ExecuteBinaryOpType` is private today). Counts: int/bool 12, str 5, float 28, set 15, bytes 54 | p34 |
| P1-2 | dict structural dunders | `dir(dict)` and `dict.__iter__/__len__/__contains__/__reversed__` (attribute path) missing. **Prereq: `PyClassInstance.GetDictStorage()`** — adding them without it recurses through the virtual `GetIterator()` (crash on `collections.OrderedDict`). `dir(list)`/`dir(tuple)` are done (p34) | p34 |
| P1-3 | descriptor classification | `list.__len__` reported as `method_descriptor`; CPython → `wrapper_descriptor` (slot). Bound forms similarly | p27 |
| P1-4 | legacy object repr | `object.__eq__` repr → `<wrapper_descriptor object at 0x…>`; CPython → `<slot wrapper '__eq__' of 'object' objects>` (new descriptors are already correct) | p34 |
| P1-5 | global `super` | `super` global is a `builtin_function_or_method`; CPython `super` is a real `type` → `super.__module__`, `type(super)`, class-attribute descriptor behavior differ (instances `super(C, x)` are correct since p24) | p24/p26 |
| P1-6 | iterator MRO | `type(iter([])).__bases__`/`__mro__` show `iterator` as base; CPython families derive directly from `object` | p25 |
| P1-7 | message wording | `tuple.index` non-integer start/stop message still `… or None, not 'X'` instead of `… have an __index__ method` | p29 |

## P2 — stdlib breadth / architectural

- `tokenize` requires `token` and `_tokenize` modules (absent).
- namespace-package `repr` parity.
- cleanup: collapse the private `PyString.Find`/`Count` fast path into the
  `TypeDict` descriptor (single source for arg-parsing instead of two).

## Confirmed fixed (was listed as residual, now closed — do not re-file)

- `reversed(b"abc")` / `reversed(bytearray)` → sequence protocol (p32).
- `del int.__len__` / setattr on native immutable types → CPython `TypeError` (p28).
- `re` non-zero `flags=` (IGNORECASE/MULTILINE/DOTALL/VERBOSE/sub) → unblocked by
  `memoryview.cast` (p33); `(?P<name>` named groups + `findall` shapes (p31/p33).
- `dir(list)` / `dir(tuple)` content sets (p34).
- `int(float)` big values (e.g. `int(1e17)`, `int(1e30)`) → BigInteger truncation
  toward zero with CPython NaN/inf messages (p35).
- `bytes` argument protocol completed and the core **moved to `PyBytes`** (mirroring
  `Objects/bytesobject.c`, which `bytearrayobject.c` delegates to): `find/rfind/index/
  rindex/count` accept an `int` byte 0..255 and any bytes-like object (`bytes`,
  `bytearray`, `memoryview`); `startswith/endswith` accept bytes-like or a tuple of them;
  `removeprefix/removesuffix` are METH_O and reject `int`. All 9 descriptors now share one
  dispatcher (`Bts`) and the same cores as bytearray — 525 lines removed, 176 added.
  Previously `bytes.find(119)` raised `TypeError`, `startswith(tuple)` was unsupported,
  and 4 args reported "takes at least 1 argument" instead of "at most 3" (p41).
- `bytearray` search/query family implemented on one shared CPython core
  (`count index rfind rindex startswith endswith removeprefix removesuffix`, plus
  `find` range validation): int(0..255)/bytes-like args, tuple prefixes, boundary match
  for the empty subsection, `ValueError: subsection not found`, CPython arity texts (p40).
- **`bytes.count(b"")` no longer hangs**: the non-overlap step `i += len - 1` decremented
  forever on an empty subsection; CPython returns `end - start + 1` (p40).
- `str.count("")` returned 0; now counts boundary positions with range respect
  (`"hello world".count("", 2, 5) == 4`) on both the descriptor and the private
  `PyString.Count` fast path (p40).
- `bytes.find/count/index/rfind/rindex` now validate start/stop via `__index__`
  (int/bool/None ok, else CPython `TypeError`) across all 7 idiom sites (p38).
- `AsString()` → renamed `ToClrString()` so `As~`=Python / `To~`=C# holds (p37).
- `AsString()` is now the Python-`str()` virtual (`ToStr()` = non-virtual alias, all 41
  subclass overrides moved) (p39).
- `__str__`/`__repr__` exceptions **propagate** instead of being swallowed into a
  fallback repr, and a non-string return raises CPython
  `__str__ returned non-string (type X)` / `__repr__ returned non-string (type X)`;
  `__str__ = None` now raises `'NoneType' object is not callable` (p39).
  Residual: generator `repr()` still lacks the CPython qualname chain
  (`<generator object <lambda>.<locals>.<genexpr> at 0x…>`) — P1 display gap.
