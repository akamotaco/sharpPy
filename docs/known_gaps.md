# SharpPy — Known CPython-parity gaps (consolidated ledger)

Maintained in one place. Per-cluster findings live in `docs/reports/<date>/pNN_*.md`;
this file is the live backlog only. Add an entry when a probe proves a divergence,
remove it (or move to a "fixed" note) when a cluster closes it.

Reference interpreter: **CPython 3.12.15**
(`C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe`).

## Verification snapshot (this state)

- `tools/evaluate.py`: `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- `tools/run_bughunt.py`: **72/72 matching**
- P0-14 closed by p64. The registration-order fix P0-14's delegation depends on is
  guarded by p65; the surface gap it exposed for other builtin types is **P0-28**.
- Every open P0 below was **re-measured 2026-10-29** (`.artifacts/tmp/p0rest.py`), so none
  of them is a stale entry. Byte argcheck (old P0-3) closed via p38/p40/p41 and is gone.
- 2026-10-30 closings after that snapshot: P0-21 (mostly), P0-23, P0-24 items 1-4, P0-22,
  **P0-25** (builtin constructor keyword signatures), **P0-27** (bytes subclasses). The table
  above still shows P0-16 as partial; p48 plus the P0-18/P0-23 sequence closed it.
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
| ~~P0-17~~ **FIXED (p47)** | regex group **numbering** | .NET numbers capturing groups with the **unnamed ones first**, so a named group declared before an unnamed one gets a different number than CPython. Measured: `re.findall(r"(?P<a>\w)(\w)", "ab")` → CPython `[('a','b')]`, SharpPy `[('b','a')]`; `m.span(1)` → CPython `(0, 1)`, SharpPy `(2, 3)`; `m.group(1)`/`group(2)` swapped while `group("a")` is correct; and `re.findall(r"(?P<a>\w) (\w) (?P=a) \2", "a b a b")` → CPython `[('a','b')]`, SharpPy `[]` (nothing matches). Fix needs Python numbering pinned at translation time - give every capture group an explicit canonical name (`(?<__g1>...)`) and rewrite `\1`/`(?P=name)`/`$n` to that name, then map `group(n)`/`span(n)`/`findall` tuples through it | p46 measured |

| ~~P0-14~~ **FIXED (p64)** | bytearray surface | the 32 bytes methods bytearray is meant to carry (`capitalize center decode expandtabs fromhex hex is* join ljust lower lstrip maketrans partition replace rjust rpartition rsplit rstrip split splitlines strip swapcase title upper zfill`) are published and return `bytearray`; `isprintable` stays absent because CPython's bytearray lacks it too. Closing note at the bottom. | p64 |

| P0-5 | static `type` | `type.foo = 1` succeeds; CPython → `TypeError: cannot set 'foo' attribute of immutable type 'type'` (SharpPy `type` is a heap `PyClass`) | p28 |

| P0-8 | `io.TextIOWrapper(buffer, encoding=…)` | wrapping an existing stream with an encoding not supported | io follow-up |
| P0-9 | importlib semantics | custom loader/spec beyond `__file__`; namespace-package behavior | p15/p18 follow-up |
| P0-16 (partial, p48) | `float` subclass payload | `class FSub(float)` instances carry no float value: `repr(FSub(2.5))` → `<__main__.FSub object at 0x…>` (CPython `'2.5'`), `FSub(2.5) + 1` → `TypeError: unsupported operand type(s) for +: 'FSub' and 'int'`, `FSub(2.5) == 2.5` → `False`, `float(FSub(2.5))` → `TypeError`. `int`/`str` subclasses work because `PyIntSubclass`/`PyStrSubclass` exist; there is no `PyFloatSubclass` | p44 measured |

Suggested order (risk × effort, cheapest real-computation wins first):
**P0-16 residuals** (reflected ops / `abs` / float descriptor self-check / hash) →
**P0-29** (builtin positional-arity message families; getattr/hasattr swallow user exceptions and exist twice) and **P0-28** (builtin type surfaces still unpublished for `frozenset`, `complex`, `memoryview`, the dict views and the iterator types) → P0-8 / P0-9 (each needs a real
feature: stream wrapping, loader protocol). P0-5 needs the static-`type` architecture
decision and is deliberately last.

Deprecated-path residuals (deliberately unimplemented; newer CPython removes them):
`int()` delegation to `__trunc__` (CPython emits
`DeprecationWarning: The delegation of int() to __trunc__ is deprecated.` then returns the
value), and `__int__`/`__float__` returning a *strict* subclass (CPython warns, SharpPy
accepts silently).

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
| P1-10 | native `re.error` identity | a `re.error` raised from native `_sre` reports `type(e).__name__ == 'ValueError'` (CPython `'error'`), and the instance has no `.pos` / `.msg` / `.lineno` / `.pattern`. `re.error` itself is correct (`error` → `Exception`, not a `ValueError` subclass), so this is the hybrid exception wrapper losing its Python class. `str(e)` matches CPython | p46 measured |
| P1-9 | unbound `object.__str__`/`__repr__` on classes | `C.__str__()` → `__str__() missing required 'self' argument`; CPython → `descriptor '__str__' of 'object' object needs an argument`, because attribute lookup on a class finds `object.__str__` before the metaclass slot. Prereq is the same `object` protocol materialization as P1-1/P22 (exposing it naively broke repr dispatch once) | p43 measured |
| P1-8 | `memoryview` slot wrappers | `hasattr(memoryview(b"a"), '__eq__' / '__ne__' / '__hash__' / '__buffer__')` is False; CPython True. The **behaviour** is now correct (p45) through CLR virtuals (`PyEquals`, `ToHash`); only the visible descriptors are missing. Deferred because exposing `__eq__` descriptors has broken CLR dispatch before (p22 lesson) | p42, behaviour fixed p45 |
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
- `memoryview` equality and hashing follow `Objects/memoryobject.c` (p45).
  `memory_richcompare` implements only `==`/`!=`; a non-view operand must export a buffer
  (bytes/bytearray do, str/int/list/tuple do not → `memoryview(b"abc") == "abc"` is plain
  `False`, not an error); `equiv_shape` compares **element counts per axis**, so itemsize is
  irrelevant and a 4-byte `'f'` view equals an 8-byte `'d'` view of the same value; items are
  compared by **decoded value**, never `memcmp`, so `NaN != NaN` stays false and `'b'` \-1 is
  not `'b'` 255. A released view answers with identity and raises nothing.
  `memory_hash` order is released → writable → non-byte-format, all `ValueError`, and a
  readonly byte-format view hashes **as the equivalent bytes object**, which is what makes
  `{b"abc": 1}[memoryview(b"abc")]` a hit (it used to be a `KeyError`).
- Caution learned the hard way (`PyObject.ToHash()` is the Python hash hook and
  `GetHashCode() => ToHash()`): a `ToHash()` that raises makes **`GetHashCode()` raise**.
  That is correct for `hash()`/dict lookups, but any C# path that formats identity through
  `GetHashCode()` then breaks — `ToString()` for `memoryview` must use
  `RuntimeHelpers.GetHashCode` directly, because CPython `memory_repr` never raises. This was
  caught by `tools/evaluate.py` (224/225: a released view's `repr` and
  `print(f"{writable_view}")`), not by the memoryview probe.
- Capture-group numbering is now CPython's, not .NET's (p47). `re/_parser.py` numbers groups by
  the position of `(` and `_compiler.py` resolves every reference to that number, so CPython's
  matcher never re-derives numbering — and SharpPy's own pure-Python compiler already produces the
  identical metadata (`ngroups`, `groupindex`, `indexgroup` measured equal to CPython) which native
  `_sre.compile` then **threw away** by re-parsing the pattern text with .NET. Since .NET assigns
  capture ids with the unnamed groups first, `(?P<a>\w)(\w)` reported the groups swapped
  (`span(1)` was `(2, 3)`) and `(?P=a) \2` stopped matching. The C# translation of "the compiler
  owns numbering" is to stop trusting engine-assigned ids: patterns that name any group are
  rewritten so each capture group carries its CPython number in a canonical name (`__pN`), and
  `\N`, `(?P=name)`, `group(n)`, `span(n)`, `start()`, `end()`, `groups()`, `groupdict()`,
  `findall()` and replacement templates all resolve through that name. `group(0)` is the whole
  match and is not a canonical name; a group that did not participate now yields `None`, not `''`
  (CPython `match_group_impl`), and an unknown group name raises plain `IndexError: no such group`.
  Patterns the scanner does not model (`(?(id)yes|no)` conditionals, `(?#comment)`, unterminated
  classes) fall back to the plain translation instead of answering with the wrong groups.
- Perf-harness caveat found while checking p47: `tools/perf_probe.py` flagged `str_fmt +30 %` and
  `list_comp +35 %` **consistently across interleaved HEAD/build A/B runs**, yet an isolated
  `%`-formatting loop measured with both binaries alternately five times each showed no difference
  (means 0.1235 vs 0.1228), and the whole `bench_suite.py` total was unchanged (median 2.900 vs
  2.869 s, min 2.788 vs 2.800). The change touches only `modules/_SreModule.cs`, which the suite
  never imports. So per-workload flags at this magnitude are an attribution artifact of
  single-process, in-process timing plus JIT warm-up, and the arbiters are (a) an isolated
  workload run against both binaries and (b) the suite total. Do not "fix" a perf_probe flag on
  its own word; and a repeated flag is *not* automatically noise either - it has to be separated
  this way.
- **P0-16 partially fixed (p48)**: added `type/PyFloatSubclass.cs` and routed float-derived
  classes through it. CPython `float_new` does
  `if (type == &PyFloat_Type) {...plain float...} return float_subtype_new(type, args, kwds);`, and
  `float_subtype_new` converts with `PyNumber_Float` *first*, then copies `ob_fval` into the
  subclass instance. Before this, `class FSub(float)` produced an ordinary object with **no value**:
  `repr(FSub(2.5))` was `<__main__.FSub object at 0x...>`, `FSub(2.5) + 1` raised `TypeError`,
  `FSub(2.5) == 2.5` was `False`, `float(FSub(2.5))` raised. `repr`/`str`/`+ - * / // % **` /
  comparisons / `bool` / `int()` / `float()` (exact `float`) / a custom `__dict__` now match
  CPython; 33 of 51 measured cases agree.
  Two structural findings while wiring it:
  * `PyClass.CreateInstance` is declared `public new`, **hiding** `PyType.CreateInstance` rather
    than overriding it, so the receiver's static type decides which implementation runs. That is why
    the first attempt (a branch in `PyType.CreateInstance`) never fired. Both entry points now call
    one helper, `PyFloatSubclass.FloatSubtypeNew`, because both are genuinely reachable.
  * Remaining divergences, all in existing dispatch gaps rather than in the subclass type itself:
    (a) reflected binary ops and `abs` - `1 + FSub(2.5)` raises `unsupported operand type(s) for +:
    'int' and 'FSub'` and `abs(FSub(2.5))` raises `bad operand type for abs(): 'FSub'`, i.e. the
    numeric fast paths recognize `PyIntSubclass` but not `PyFloatSubclass`;
    (b) float's own descriptors refuse subclass instances - `format(x, '.2f')`, `x.is_integer()`,
    `x.as_integer_ratio()`, `x.hex()` all raise
    `descriptor 'NAME' requires a 'float' object but received a 'FSub'`, while CPython's
    `PyFloat_Check` accepts subclasses (same class of bug as `%b` bytes subclasses and the `upper`
    case); a central "self-check accepts instances" rule would close these and should be measured
    against int/str/bytes descriptors before changing;
    (c) argument validation on the subclass path is still not enforced: CPython says
    `FSub(x=1)` -> `float() takes no keyword arguments` and `FSub(1, 2)` ->
    `float expected at most 1 argument, got 2`, while SharpPy accepts both (`FSub(1, 2)` answers
    `1.0`). The guard exists in `PyFloatSubclass.FloatSubtypeNew` but neither `CreateInstance`
    entry point is reached for those calls, which is the third evidence that user-class calls have
    more than one dispatch route; finding the real route is the first task when this resumes.
    Related, same family: a user `__new__` that calls `float.__new__(cls, v)` fails with
    `type.__new__() takes exactly 4 arguments (metaclass, name, bases, dict)`, so `float.__new__`
    is not materialized as a usable slot, and `round(FSub(2.5))` says
    `type FSub doesn't define __round__ method`.
    (d) `hash(FSub(2.5)) == hash(2.5)` is `False`, so `{2.5: 'v'}[FSub(2.5)]` raises `KeyError` and
    `len({FSub(2.5), 2.5})` is 2 - `__hash__` is not resolvable through the MRO for `float`, which is
    the same non-materialized-slot gap as P1-8, not something the subclass type can fix alone.
- **P0-18 (new, measured): builtin-subclass handling is systemic, not per-type.** A fresh 18-case
  probe of one theme (`class I(int) / S(str) / Bs(bytes) / L(list)`) produced 5 divergences across 4
  builtin types, none previously numbered:
  * `I(8).bit_length()` -> `descriptor 'bit_length' requires a 'int' object but received a 'I'`
    while CPython's `PyLong_Check` accepts subclasses; every builtin method descriptor that
    validates `self` with an exact CLR type test has this bug (this is the same shape as the float
    residuals in P0-16 and the `%b` bytes-subclass case).
  * `1 + I(5)` -> `unsupported operand type(s) for +: 'int' and 'I'` and `round(I(5))` ->
    `type I doesn't define __round__ method`. So the reflected-operator and special-method
    reachability gaps are **not** float-specific; `PyIntSubclass` has them too.
  * `format(Bs(b"ab"), ">4")` names the wrong class - CPython says
    `unsupported format string passed to Bs.__format__`, SharpPy says `bytes.__format__`.
  * `repr(L([1, 2]))` -> `<__main__.L object at 0x...>` instead of `[1, 2]`, i.e. a `list`
    subclass has the same "no value surface" shape as P0-16 even though `len()`/`reversed()` work.
  Fixing this centrally (a `PyLong_Check`-style "self may be an instance" test plus materializing
  the special-method slots that `round`/`hash` need) is likely to close more remaining P0s than any
  further per-type work, and it also unblocks the P0-16 residuals and P1-8.
- P0-18 increment 1 (commit 23cd41d): 9 exact `self is PyInt` gates in `type/PyInt.cs` now go
  through `PyInt.TryLongSelf`, which is the CLR form of `PyLong_Check` (accept the subclass, read
  the stored value). `I(8).bit_length()`, `I(5).real/.imag/.numerator/.denominator`, etc. match.
  P0-18 increment 2 (commit ceb513e): same treatment for `type/PyFloat.cs` via `PyFloat.TryFloatSelf`.
  That closed `format(FSub(2.5), '.2f')`, `f"{x:.2f}"`, `x.is_integer()`, `x.as_integer_ratio()`,
  `x.hex()` - the float matrix went from 34 divergent lines to 22 (51 cases).
  P0-18 increment 3 (this commit): reflected operators closed for int. The cause was not a missing
  attribute - `int.__radd__` exists and `hasattr(I(5), '__radd__')` is already `True`; the
  `regIntBin`/`regIntRev`/`regIntUn` wrappers in `type/PyInt.cs` passed the *subclass object* into
  `PyInt.Add`, whose exact-type fast path answered `NotImplemented`, so `1 + I(5)` became a
  TypeError. They now pass `AsIntObj(self)` / `AsIntObj(args[0])`, which is CPython's behaviour:
  the reflected slot reads self's digits and the result is a plain int. `1 + I(5)`, `1 - I(5)`
  (order correct: `I(5).__rsub__(1) == -4`), `I(5) + I(6)`, `I(5).__rtruediv__(1)` all match.
  Still open in P0-18, measured: `int.__round__` and `float.__round__` are not registered at all
  (`hasattr(int, '__round__')` False vs CPython True) so `round(I(5))` fails; `float.__hash__` is
  not registered (`hasattr(float, '__hash__')` False) which is why `hash(FSub(2.5)) != hash(2.5)`;
  `1 + FSub(2.5)` still needs the same treatment on the float side; `abs(FSub)`,
  `"%.3f" % FSub`, list-subclass `repr`, and `format(Bs(b'ab'), '>4')` naming `bytes.__format__`
  instead of `Bs.__format__`.
- **P0-19 RETRACTED - it was my broken probe generator, not SharpPy.** I claimed a caught exception
  escaped after ~20 handled exceptions in one file. Re-testing with a proper driver (asserting on
  `DONE` and on the actual exception text instead of counting the word `Traceback`) showed every
  variant failed for reasons of my own making: `src.split("s(", 1)` cut the file inside the probe's
  own `def s(l, f):` line, producing a file whose `class`/`def` structure was malformed (a CLR parse
  failure in `Parse_ClassDefRaw`), and another variant had no helper at all, so it failed with
  `NameError: name 's' is not defined`. On the one file that did run to completion
  (`tests/bughunt/p49_subclass_gates.py` plus an appended case, CRLF/LF mixed), **CPython escapes in
  exactly the same way with the same message**, so there is no SharpPy-specific divergence there.
  Lesson recorded where it belongs: a reproducer must assert on the observable (`DONE`, exception
  type, message), never on the presence of a word in stderr, and it must be diffed against CPython
  before being called a SharpPy bug.

- **라이브러리 항목 스코프 정책 (2026-10-29).** 표준 라이브러리 전면 호환은 목적이 아니고, 필요한
  surface를 C# 특징을 고려해 구현하는 방향으로 확정했다. 구성 측정: `Lib` 41개 파일 18,922줄, native C#
  module 20개. `math`는 `Lib/math.py`가 아예 없는 C# 전용이고, `abc`/`_collections_abc`/`io`/`os`/`re`/
  `types`/`typing`은 Python이 native `_module`에 위임하며, `enum.py`(2108=2108)와 `re/__init__.py`
  (385=385)는 CPython 그대로다. `io.py`(302 vs 99)와 `types.py`(396 vs 333)은 CPython보다 커서 shim이
  붙어 있다. 따라서 **`tempfile` 미존재는 결함이 아니라 의도적 미구현**으로 재분류하고, 필요 시 CLR의
  `Path.GetTempPath`/`GetRandomFileName`을 쓰는 C# native module로 만든다. P0-8(`io.TextIOWrapper`
  생성자)과 P0-9(importlib loader)도 `Lib` 파일 교체가 아니라 `IoModule.cs` / `ImportlibModule.cs`
  진입점에서 해결한다. 상세: docs/library_surface_policy.md

- **P0-18 increment 5 (measured): `float` now publishes its numeric slots, and `sum` stops leaking
  the `NotImplemented` sentinel.** CPython's `float_as_number` (Objects/float.c, wired through
  Objects/slotdefs.c `nb_reflected`) exposes `__radd__`/`__rsub__`/`__rmul__`/`__rtruediv__`/
  `__rfloordiv__`/`__rmod__`/`__rpow__`/`__rdivmod__` plus `__abs__`/`__neg__`/`__pos__`/`__floor__`/
  `__trunc__`/`__ceil__` as slot wrappers that read the value payload. SharpPy registered **none** of
  them on `float`, so a subclass that carries a real float value (p48) still failed:
  `1 + FSub(2.5)` -> `TypeError: unsupported operand type(s) for +: 'int' and 'FSub'`.
  Registered through the same wrapper shape as the int family, reading `self` via `TryFloatSelf`, which
  is the CLR counterpart of `nb_reflected` reading `ob_fval`: the subclass contributes its value and
  the result stays a plain `float`. `sum()` was separately wrong: it accumulated with the virtual
  `Add` and, when that returned `NotImplemented`, fed the sentinel into the next iteration, producing
  `unsupported operand type(s) for +: 'NotImplementedType' and ...`; it now tries the reflected
  operand and raises CPython's message if that is also `NotImplemented`.
  Still divergent for a float subclass, measured: `abs(FSub(2.5))`, `divmod(FSub(2.5), 2)` and
  `"%.3f" % FSub(2.5)`. `abs()` was changed to dispatch through `__abs__` for non-exact receivers, but
  the lookup returns nothing for a `PyFloatSubclass`, so the remaining problem is that this CLR class
  is not reachable through the special-method lookup machinery - it is not a missing slot anymore.
  Verified: `tests/bughunt/p52_float_numeric_slots.py` (22 lines, diff 0), bughunt 57/57, evaluate
  unchanged 225/3/2, Godot replay 82 matched 0 other.

- **P0-20 CLOSED: builtin keyword arguments now follow CPython's METH_KEYWORDS table.** CPython
  rejects keywords on METH_O / METH_VARARGS / METH_FASTCALL builtins with
  `{Type}.{name}() takes no keyword arguments`; SharpPy dropped the keywords and returned a value, so
  `(1).bit_length(x=1)` answered `1` instead of raising. Root cause was a dead field:
  `PyMethodDescriptor` took `acceptsKwargs` and stored it, but nothing ever read it.
  What the rollout needed (the first attempt, a guard alone, broke 9 of 55 probes):
  * the reject guard in `PyMethodDescriptor.Call`, `CallWithReceiver` and
    `PyBoundMethodDescriptor.Call`, plus the same rejection in `PyStringMethod.Call`;
  * `__new__` excluded from the generic message, because CPython answers it from the type-call
    machinery with different labels (registered below as P0-21);
  * `acceptsKwargs: true` on exactly the measured set: `dict.update`, `str.format`, `str.format_map`,
    `str.split`, `str.rsplit`, `str.encode`, `str.expandtabs`, `str.splitlines`, `bytes.decode`,
    `bytes.splitlines`, `int.to_bytes`, `memoryview.cast`;
  * keyword projection (`PyObject.KwValue`, `PyString.ProjectKeywordSlots`) for delegates that only
    read the positional array - flagging alone made `to_bytes(length=2, byteorder='little')` raise
    `expected 1 to 2 arguments, got 0` and `splitlines(keepends=True)` silently drop the flag;
  * `PyDict.GetItem` raises KeyError on a missing key, so a keyword lookup has to test presence
    first; otherwise an absent keyword turns into `KeyError 'length'`.
  Two findings that only the full audit produced:
  * **`PyString.GetAttribute` shadowed the type.** Its switch returned instance closures for upper,
    lower, title, strip, lstrip, rstrip, replace, split, join, find, count, encode, isidentifier -
    all thirteen of which are registered as descriptors in `strType.TypeDict`. The shadow made the
    duplicate closure path authoritative for keyword acceptance, arity messages and descriptor
    identity. It is deleted; `base.GetAttribute` (MRO + descriptor) now answers, and the bound form
    stays CPython-correct (`type('a'.upper).__name__ == 'builtin_function_or_method'`,
    `<built-in method upper of str object at 0x...>`).
  * **the shadow was hiding two wrong implementations**, which surfaced as soon as the descriptors
    became reachable: `strType.TypeDict['title']` used `TextInfo.ToTitleCase` (.NET word-breaking) and
    answered `It's A Test-Case 2Nd` where CPython gives `It'S A Test-Case 2Nd`; and
    `PyString.StrEmptySpan` returned a negative count for a reversed range where CPython's
    `"abc".count("", 3, 1)` is 0. Both now use the CPython-shaped implementations.
  Cost, measured rather than assumed: 1.6M str attribute lookups in 400k iterations, old and new
  binaries alternated - old median ~2.989 s, new median ~3.062 s, about **+2.4 %**. `perf_probe`
  showed no flagged workload (`str_fmt +1.0 %`). Follow-up if that matters: cache the descriptor
  lookup instead of walking the MRO per access.
  Verified: `tests/bughunt/p51_keyword_args.py` (30 lines, diff 0), bughunt 55/55, evaluate unchanged
  225/3/2, Godot replay 82 matched 0 other.
  Corrections to this ledger's earlier draft: it claimed `str.split`, `str.rsplit`, `str.encode` and
  `str.expandtabs` reject keywords. Measured on CPython 3.12.15 they accept them
  (`split(sep=, maxsplit=)`, `encode(encoding=, errors=)`, `expandtabs(tabsize=)`). The claim came
  from reading a diff instead of the interpreter.

- **P0-21 (2026-10-30 대부분 닫힘): `object_new` / `object_init` 인자 규칙.**
  **이 항목의 등록 문장이 틀렸다.** "plain `A(x=1)`는 이미 맞다"고 적었지만 실제측정은
  SharpPy가 인자를 **조용히 무시하고 인스턴스를 만들었다** — 메시지 문제가 아니라 실행 결함이었다.
  그래서 메시지만 고치는 작업이 아니라 `type_call` 규칙을 넣는 작업이 됐다.

  소스에서 정확한 규칙(`Objects/typeobject.c:5416-5457`): 판정은 두 개의 술어 —
  `tp_new == object_new`인가, `tp_init == object_init`인가. CLR에서는 "MRO가 object 고유의
  디스크립터로 해석되는가"로 번역했다. 구현 후 일치:
  * `A(1)` / `A(x=1)` / `A(1,2)`          -> `TypeError: A() takes no arguments`
  * `C(x=1)` (user `__init__(self,x)`)   -> object_new는 침묵, user 함수가 자기 arity를 보고
  * `object.__new__(A, 1)` / `(A, x=1)`  -> `A() takes no arguments`
  * `object.__init__(A(), 1)`            -> `A.__init__() takes exactly one argument (the instance to initialize)`
  * `object.__init__(C(1), 2)`           -> `object.__init__() takes exactly one argument (the instance to initialize)`
  * 내장 기반(`dict`/`list`/`int`)은 자기 `tp_new`가 인자를 받으므로 object_new에 닿지 않는다
    — `MD(a=1)`류가 새로 깨지지 않는 것을 probe로 고정.
  회귀: `tests/bughunt/p61_object_new_init_rules.py` 37케이스 diff 0. repr 주소는 별도 표시 결함이라
  probe에서 `<instance>`로 정규화했다.

  **남은 것(builtin 전용 `__new__` 인자 문구):**
  * `tuple.__new__(tuple, iterable=[])` -> CPython `tuple() takes no keyword arguments`, SharpPy `()`
  * `H("ab", encoding="utf8")` (str subclass) -> CPython `decoding str is not supported`
  * `str.__new__(H, 5, 6)` -> CPython `str() argument 'encoding' must be str, not int`
  일반 규칙과 달리 각 builtin의 `__new__` 인자 검사(`PyArg_Parse*` 형식)를 옮겨와야 한다.

- **P1-새(2026-10-30): object repr의 주소 형태.** CPython `<__main__.A object at 0x000002730A8DE3C0>`
  vs SharpPy `<__main__.A object at 0x1189baa>`. 값이 아니라 표시 형식 차이이지만 repr 대조 probe에서
  주소를 정규화해야 하므로 남긴다. (주소 자체는 원래 휘발성 — 형태만 문제.)

- **P0-22 (2026-10-30 **닫힘**): `int.to_bytes` / `int.from_bytes`의 64비트 이상.** 예측보다 나빴다.
  `to_bytes`는 `(long)` 캐스트 때문에CLR `System.OverflowException`을 **그대로 흘렸다** — Python
  예외가 아니므로 트레이스백이 아니라 CLR 스택으로 프로세스가 죽었다(`to_bytes` 하나로 스크립트
  전체가 중단). 이제 BigInteger 경로이고 용량 검사도 CPython식(bit 수 비교 + signed일 때 부호비트):
  `2**70` 10바이트, signed 2의 보수, `(0).to_bytes(0)` 포함. 회귀 probe `p60` 42케이스 diff 0.
  조사 중에 **장부에 없던 조용한 오답 두 개**가 나왔다:
  1. `int.from_bytes`누적자가 `long`이라 넓은 입력에서 **조용히 0**을 반환했다.
     `int.from_bytes((2**70).to_bytes(10,'big'),'big')` → CPython `1180591620717411303424`,
     SharpPy `0`. 예외조차 없었다.
  2. `signed` 키워드 파싱이 **아예 없고** 상위 비트를 무조건 부호로 읽었다.
     그래서 `int.from_bytes(b'\xff','big')`는 CPython `255`인데 SharpPy는 `-1`. 반대 케이스
     (`signed=True`)는 키워드 미지정으로 깨지거나 같은 값으로 우연히 맞았다.
  3. `byteorder=` 키워드가 positional만 보는 delegate에서 무시됐다
     (`int.from_bytes(b'\x01\x00', byteorder='little')` → 256, CPython 1).
  동시에 bytes-like 인수도 CPython처럼 넓혔다(`bytearray`/`memoryview`/int iterable, str은
  `cannot convert 'str' object to bytes`로 거부, 요소 오류는 `'X' object cannot be interpreted as
  an integer`, 범위 밖은 `bytes must be in range(0, 256)`).
  교훈: **예외 타입이 CLR이면 테스트가 아예 실행되지 않는다.** 같은 코드라인에서 조용한 오답이
  두 개 더 나온 것은, 크래시가 가시성을 없애는 동시에 인접 결함도 함께 숨긴다는 뜻이다.


- Named backreferences work. `(?P=name)` → `\k<name>` was already being emitted, but through
  `Regex.Replace(pattern, ..., @"\\k<$1>")`, and the **replacement argument of .NET
  `Regex.Replace` is not a regex** — only `$` is special there — so it wrote a literal
  backslash followed by `k`, which .NET parsed as an escaped `k`. Every pattern containing
  `(?P=name)` therefore matched nothing at all (`[]` / `None`) instead of raising. One
  backslash fixes it (p46).
- `re.sub()`/`subn()` replacement templates are now translated by `ToDotNetReplacement`, which
  follows `re/_parser.py parse_template` rather than handing the Python template to .NET:
  `\g<name>` and `\g<1>` → `${name}`/`${1}`, `\1`–`\99` group refs (max two digits), `\0` as
  octal NUL **not** group 0, the C escapes `\a \b \f \n \r \t \v`, `\x`/`\u`/`\U`, a dropped
  backslash before punctuation, literal `$` doubled to `$$` for .NET, and
  `bad escape \q at position 0` for unknown letter escapes. Previously every template came out
  verbatim, so `re.sub(r"(\w)", r"[\1]", "ab")` produced `[\1][\1]` instead of `[a][b]` (p46).
- Numeric coercion dunders now follow `Objects/abstract.c`. `PyNumber_Long` order is
  exact-int → **`nb_int` (`__int__`) → `nb_index` (`__index__`)** → float → str/bytes, and
  `PyNumber_Float` is **`nb_float` → `nb_index`** and never `__int__` (measured on the same
  object: `int(Both()) == 70`, `float(Both()) == 30.0`). The earlier ledger note had the
  `int()` order backwards; the C source and the measurement agree that `__int__` wins.
  Validation texts are CPython's: `__int__ returned non-int (type X)`,
  `{Type}.__float__ returned non-float (type X)` (prefixed with the object's own type), and
  `bool` / int-subclass results are accepted because `PyLong_Check` covers them.
  SharpPy implements native `nb_int`/`nb_float` as CLR virtuals, so the user-class half
  lives in `PyClassInstance.AsInt/AsFloat` (p44).
- `float()` fixed at the same entry point: it rejects keywords
  (`float() takes no keyword arguments` — `float(x=1)` used to return `0.0`), its arity text
  is `float expected at most 1 argument, got N`, and every buffer form parses as text
  (`float(b"2.25")`, `bytearray`, `memoryview`) with CPython's message that reports the
  **object** (`could not convert string to float: b'x'`). The shared parser
  (`PyFloat.TryParsePythonText`) accepts PEP 515 underscores only between digits
  (`1e1_0` → 1e10; `_1`, `1__0`, `1_`, `1._5` rejected) and parses with the invariant
  culture instead of the machine locale. Huge `__index__` results keep raising
  `int too large to convert to float` instead of returning infinity (p44).
- Metaclass `__str__`/`__repr__` now govern class objects: `PyType.ToRepr/AsString` walk the
  metaclass MRO and stop at `type`/`object` (whose slots *are* the default `type_repr`).
  `str(C)`, `repr(C)`, `f"{C}"`, `format(C)`, `"%s" % C`, `print(C)`, `[C]`, `{C: 1}` and
  `(C,)` all match, including inheritance through `class M2(M)` and subclasses, the
  `__str__ returned non-string (type int)` / `__repr__ returned non-string (type int)`
  guards and exception propagation from a metaclass `__str__`. CPython has neither
  `type.__str__` nor `type.__format__` (both resolve to `object`'s), so `type.__str__(C)`
  follows `tp_repr` → `'META-repr:C'`, `format(C, ">20")` raises
  `unsupported format string passed to M.__format__` naming the **metaclass**, and
  `type.__repr__(C)` still gives the static `<class '__main__.C'>` (p43).
- `memoryview.cast` now follows `memory_cast`: only a native single struct character with
  an optional `@` (so `=`, `<`, `>`, `!` are rejected as CPython does), `format`/`shape`
  keywords, `argument for cast() given by name ('format') and position (1)`, `cast() takes
  at most 2 arguments (N given)`, `cast() missing required argument 'format' (pos 1)`,
  `cast() argument 'format' must be str, not X` (with CPython's `None` spelling), and shape
  validation (`elements of shape must be integers` / `... > 0` / `shape must be a list or a
  tuple`).
- Real **N-D memoryviews**: `cast(fmt, shape)` builds row-major views with correct `ndim`,
  `shape`, `strides` (`(8, 4)` for a 2×2 `'I'` view), `len()` = first axis, nested
  `tolist()`, full-rank tuple indexing with `index out of bounds on dimension N`, and
  CPython's own refusals (`multi-dimensional sub-views are not implemented` for an int
  index, `sub-views are not implemented` for short tuples, `cannot index 2-dimension view
  with 3-element tuple`). Dim-0 slicing keeps the rank (`v2[0:1]` → `[[1, 2]]`, ndim 2).
  Also exposed `c_contiguous`/`contiguous`/`readonly`/`suboffsets`/`obj` and materialized
  `__getitem__`/`__setitem__`/`__len__` slot wrappers (p42).
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

## 2026-10-29 io B4 과정에서 추가된 잔여

- **P1**: native 모듈 `__file__` 값이 플레이스홀더다. `_io.py`, `time.py`,
  `<builtin sys module>` 처럼 파일이 아닌 값이 보이고, 네이티브 상수에는 빌드 머신 절대 경로가
  박혀 있다. `from X 없는이름` 메시지는 실재 파일만 origin으로 인정하도록 gating 해서 측정값과
  일치시키지만(같은 날짜 보고서), `__file__` 자체는 여전히 틀리다.
- **P1**: `_collections_abc.__name__` 이 `collections.abc`가 아니다. CPython에서는 두 이름이 같은
  모듈 객체를 공유하므로 `cannot import name 'NOPE' from 'collections.abc'` 처럼 나온다.
- **P1**: `io.IOBase.__doc__` 등이 `None`. CPython `Lib/io.py`는
  `__doc__ = _io._IOBase.__doc__` 로 native docstring을 public ABC에 건다. SharpPy native 타입에
  `__doc__`가 없어서 해당 라인을 생략한 상태다(표시 전용).
- **P2**: `_io._WindowsConsoleIO` 미구현. Windows CPython에는 있다. `Lib/io.py`의
  `try: from _io import _WindowsConsoleIO / except ImportError` 가 의도대로 동작해서
  `import io` 는 깨지지 않는다.

## 2026-10-29 성능 관측 (io B4 직후)

- **P1(성능, attribution 미완료)**: `tools/perf_probe.py`의 `comp_lambda` +10.5~11.1%,
  `call_heavy_fib20` +8.6~10.7%가 3회 실행에서 재현된다. baseline `int_loop`도 +2~3%라 기계
  드리프트가 깔려 있고, 수정한 경로는 속성설정 실패/import 실패/`open` 정적화뿐이라 hot path와는
  무관하다. 이전 바이너리와 A/B해서 귀속을 확정해야 한다.
- **P1(성능)**: 모듈 import 비용. 측정값(프로세스 5회 min): `print("x")` 229.9 ms,
  `import io` 278.0 ms(+48), `import abc` 265.5 ms(+36), `import json` 829.2 ms(+600).
  CPython은 같은 항목이 23.3/18.8/18.7/32.8 ms다. 특히 json 600 ms가 크다.

### 2026-10-30 attribution 닫힘 — 위 P1(성능, attribution 미완료)는 B 시리즈 소행이 아니다

pre-B 리비전(`9f1c366`)을 별도 worktree에서 같은 플래그로 빌드하고, cwd는 메인 저장소로 고정한 채
두 바이너리를 교대로 2라운드 측정했다:

| workload | pre-B | HEAD | 차이 |
|---|---|---|---|
| `comp_lambda` | 0.0335 / 0.0340 | 0.0345 / 0.0358 | 약 +4% |
| `call_heavy_fib20` | 0.0198 / 0.0197 | 0.0199 / 0.0202 | 약 +2% |
| `int_loop` | 0.2140 / 0.2153 | 0.2145 / 0.2159 | 약 +0.4% |
| `dict_ops` | 0.0615 / 0.0598 | 0.0606 / 0.0618 | 약 +0.6% |

pre-B만으로도 `comp_lambda`가 이미 +6.3~7.9%다. 즉 플래그의 대부분은 baseline 스테일/머신 드리프트이고,
HEAD와 pre-B의 차이는 같은 바이너리를 라운드별로 재측정했을 때의 산포(HEAD 자체 0.0345→0.0358 = 3.8%)
안에 있다. 할 일은 없고 대신 micro workload 두 개(`comp_lambda`, `call_heavy_fib20`)의 baseline이
노이즈 바닥에서 갱신돼야 한다는 것이 남았다. 모듈 import 비용 항목은 여전히 유효하다.

## P0-23 (2026-10-30 **닫힘**): builtin 숫자 subclass의 dunder 디스패치 잔여

같은 날짜 두 커밋으로 전부 닫았다: `e70c3ea`(abs/index/% 서식/슬롯 등록/프레임 우선순위)과
이번 커밋(아래 1~5). 회귀 probe `tests/bughunt/p58_numeric_subclass_dunders.py` 33케이스 diff 0.
보고서 `docs/reports/2026-10-30/numeric_subclass_dunders.md`.
핵심은 `PyObject.LookupUserSpecial` — user 프레임(PyClass.ClassDict)에서만 찾고 native 타입에서
멈춘다. MRO 전체를 조회하면 base 슬롯이 같은 CLR virtual로 되 들어와 무한 재귀가 된다.
`divmod(2, IPlain(5))`는 `(0, 2)`, `divmod(2, FSub(2.5))`는 `(0.0, 2.0)`,
`math.floor(FO)`는 `'user-floor'`, `math.floor(PF)`는 `0`, `f + 2`는 `'user-add'`.
`float.__int__`/`float.__float__` 슬롯도 등록(`__float__`는 self를 그대로 반환).

남은 계열(같은 병리, 다른 family): **str subclass**가 VM fast path에서 자기 `__len__`/`__iter__`/
`__contains__`/`__reversed__`를 잃는다. `tests/bughunt/p49_subclass_gates.py`가 SKIP으로 기록.

---

### 등록 당시 기록(이력은 아래 측정을 남긴다)

같은 계열의 절반(`abs`, index 계열, `%` 서식, 슬롯 등록, 프레임 우선순위)은 같은 날짜 보고서로 닫았다.

같은 계열의 절반(`abs`, index 계열, `%` 서식, 슬롯 등록, 프레임 우선순위)은 같은 날짜 보고서로 닫았다.
측정되고 남은 것:

1. `divmod(FSub(2.5), 2)`, `divmod(2, FSub(2.5))`, `divmod(ISub(5), 2)` → CPython은 `(1.0, 0.5)` /
   `(0.0, 2.0)` / `(2, 1)`. SharpPy는 TypeError. wrapper 타입(`PyFloatSubclass`, `PyIntSubclass`)이
   CLR `DivMod`를 오버라이드하지 않아 dunder 조회 자체에 닿지 않는다.
2. `math.floor/ceil/trunc(FSub)` → CPython은 user dunder를 존중한다(`'user-floor'`). SharpPy는
   `must be real number, not FO`. `MathModule`의 정확 타입 검사.
3. `int(FSub)`는 user `__int__`를 존중(측정 77), `float(FSub)`는 user `__float__`를 존중(측정 9.5).
   SharpPy는 payload만 쓴다(2, 2.7). 단 **`operator.index`는 반대**인데, 그건 이미 맞췄다(int
   subclass는 long의 값 슬롯).
4. 산술 user dunder 우선순위 — `class F(float): def __add__` 를 CPython은 `'user-add'`로, SharpPy는
   `4.5`로 답한다. `PyFloatSubclass`/`PyIntSubclass`의 산술 오버라이드가 payload로 곧장 가기 때문.
5. `float.__int__`, `float.__float__` 슬롯 미등록(CPython에는 존재).

재현 파일(측정 당시): `.artifacts/tmp/subdunder.py`, `.artifacts/tmp/subslots.py`.
지속 회귀로 승격: `tests/bughunt/p58_numeric_subclass_dunders.py`.

## P0-24 (2026-10-30 등록): container subclass dunder 디스패치 잔여

`e70c3ea`/`054eb8c`(숫자)로 증명한 병리가 container 계열에도 있다. 같은 측정 메서드로 범위를
잡았다: `class X(기반): def __dunder__` 형태에서 CPython은 **선언된 dunder**를 부르고, SharpPy는
**payload 슬롯**을 부른다. 재현 파일 `.artifacts/tmp/container_sub.py`.

수정 완료(지속 회귀 `tests/bughunt/p59_container_subclass.py`, 34케이스 diff 0):

- `len(dict subclass)` → CPython은 `1`. SharpPy는 `TypeError: object of type 'DPlain' has no len()`.
  `PyClassInstance.Length()`가 `_strStorage`만 참조했고 `_dictStorage`/`_listStorage`를 못 봤다.
  이건 값이 틀린 게 아니라 **깨진 동작**이었다.
- 선언 dunder 우선순위를 `Length()`/`Contains()`에 넣었다(storage 조회가 먼저라서 클래스의
  `__len__`/`__contains__`가 무시되던 순서 문제).
- `PyStrSubclass`의 `__add__`/`__mul__`/`__mod__`/`__len__`/`__contains__`/richcompare → user dunder
  우선. 수정 전 `SUser("abc") + "z"`는 `'abcz'`, `su == "z"`는 `False`였다.
- `__len__` 반환값은 CPython처럼 `__index__`로 변환해 검증한다(`'str' object cannot be interpreted
  as an integer`).

남은 것(측정값, 7줄):

1. **str subclass의 `__iter__`** — **수정 완료**. 원인은 조수(assistant)가 만든 헬퍼 오용이었다:
   `CallUserSpecialOrNull`은 조회+**호출** 헬퍼인데, 그 결과(list_iterator)를 다시
   `InvokeIteratorMethod`에 넣었다 → iterator를 호출했다. 크래시를 외부 경로로 오인하고
   payload로 되돌린 것도 잘못이었다. 조회 전용 `LookupUserSpecial` + `InvokeIteratorMethod`로
   해결(`p59`에 `s_iter`/`s_iter_for`/`s_iter_type` 고정).
2. **int subclass의 `__iter__`** — **수정 완료**. `PyIntSubclass`/`PyFloatSubclass`에
   `GetIterator()`를 넣었다: 선언 dunder 우선, 없으면 `'<subclass>' object is not iterable`
   (기반 타입명이 아니라 **서브클래스명**을 낸다는 것도 측정했다).
3. **tuple subclass** — **수정 완료**(`t_len 99`, `t_iter ['t']`, `t_add 'user-tadd'`).
   추정과 달리 instance는 `PyTupleSubclass : PyTuple`이었고, `_customType`이 아니라
   `InstanceType`으로 타입을 추적한다. 그래서 첫 시도의 `HasCustomType` 가드가 아무것도 잡지 못했다.
   `LookupUserSpecial`은 첫 non-PyClass MRO 프레임에서 멈추므로 가드 없이도 exact tuple은
   `GetPyType()` + 타입 검사 한 번이다. `list(t)`는 `AsList()`가 `Items`를 직접 복사해 우회하고 있었
   다 — CPython의 `list()`는 `tp_iter`를 거친다는 점이 수정 근거였다. hot path라 A/B 교대 측정:
   TOTAL `+0.3%`, tuple 관련 workload 차이 없음.
4. **bytes subclass** — **원인이 예상과 달랐다.** dunder 디스패치가 아니라 **생성 시 subclass가
   소실**한다: `class B(bytes): pass` 에서 `type(B(b"xy")).__name__` 이 CPython은 `'B'`, SharpPy는
   `'bytes'`. 따라서 어떤 dunder도 도달할 수 없고 `len`/`__iter__`/`__add__` 분기는 전부 이 하나의
   결함에서 나온다. `PyBytesSubclass`는 존재하지 않고(`PyTupleSubclass`는 존재), bytes 생성 경로는
   class를 버린다. 재현 `.artifacts/tmp/tbsub.py`. wrapper와 생성 경로를 함께 만들어야 하는 별건.

3번과 4번은 hot path에 user-frame 조회를 추가하므로 `_customType == null` 조기 반환으로 가두고
측정해야 한다. A/B 교대 재측정(`.artifacts/tmp/ab/ab_run.py`, 5라운드) 결과 이번 container 수정은
회귀가 없었다: `dict_ops -0.2%`, `closure_calls -2.4%`, TOTAL `+0.1%`. `perf_probe`가 먼저
`dict_ops +15.6%`/`closure_calls +21.5%`로 플래그했지만 라운드 0의 편차(list_comp 0.0578↔0.1026)가
보여주듯 산위가 크다 — 플래그는 세 번째 반복해서 기각된 셈이다.


## P0-25 (2026-10-30 **닫힘**): builtin 생성자의 keyword 시그니처 — kwargs가 **버려지던** 경로

`PyType.CreateInstance` 끝부분이 이랬다:

```csharp
var builtinFunc = new PyBuiltinFunction(Name);
return builtinFunc.Call(args, null);   // kwargs = null
```

즉 special-case된 타입(float/int/set/…)을 제외한 모든 builtin 생성에서 keyword 인자가 **사라졌다**.
측정 결과 이것은 메시지 결함이 아니라 **조용한 오답**이었다:

| 표현 | CPython 3.12.15 | SharpPy (수정 전) |
| --- | --- | --- |
| `dict(a=1)` | `{'a': 1}` | `{}` |
| `dict([("a",1)], b=2)` | `{'a': 1, 'b': 2}` | `{'a': 1}` |
| `str(object=5)` | `'5'` | `''` |
| `str(nope=1)` | `TypeError: 'nope' is an invalid keyword argument for str()` | `''` |
| `bool(x=1)` | `TypeError: bool() takes no keyword arguments` | `False` |
| `complex(real=1, imag=2)` | `(1+2j)` | `0j` |
| `bytes("é", encoding="utf8")` | `b'\xc3\xa9'` | `TypeError: 'str' object cannot be interpreted as an integer` |
| `str(b"ab", "utf8")` | `'ab'` | `"b'ab'"` |

`str` 계열은 `encoding`/`errors` 자체도 죽어 있었다 — `H(b"ab","utf8")`이 `str()`으로 재버전된
`"b'ab'"`를 반환했다. `p48` 이후 subclass 생성을 만져왔지만 이 경로는 아무도 건드리지 않았다.

### CPython의 두 가지 거절 문형

측정으로 고정한 표(signature의 positional-only 여부):

- **keyword 절대 불가** → `X() takes no keyword arguments`: `tuple`, `list`, `set`, `frozenset`,
  `bool`, `float`, `range`, `slice`
- **명명된 파라미터 있음** → `'x' is an invalid keyword argument for X()`: `str`(object/encoding/
  errors), `bytes`·`bytearray`(encoding/errors), `complex`(real/imag)
- `dict`는 임의 keyword를 **항목으로**accept한다.

두 문형을 혼동하면 안 된다. 전자(`tuple(iterable=[])`)에 후자 문형을 쓰면 그것이 곧 새 bugs가 된다.

### 구현

- `PyType.CheckConstructorKeywords` — 위 표. hot path에서는 `kwargs == null` 조기 반환 한 번.
- `str` 생성과 str subclass 생성이 같은 `PyStringNewArguments`(unicode_new 문법)를 공유한다.
  `Project(args, kwargs, hasReceiver)`로 `str.__new__`(args[0]이 수신 클래스)와 `str(...)`를 한
  구현으로 처리한다.
- bytes/bytearray는 text+codec 경로를 `PyBytesTextConstruct`로 만들었다. text에 encoding 없이
  `string argument without an encoding`을 내는 것까지 CPython과 일치.
- `tuple.__new__`는 CPython `tuple_new`의 세 거절을 모두 갖췄다: keyword 불가, positional 1개 초과
  불가(`tuple expected at most 1 argument, got N`), 그리고 **tuple 하위가 아닌 클래스**
  (`tuple.__new__(H): H is not a subtype of tuple`). 마지막 것은 `cls.GetTypeName()`이 `type`을
  반환해서 메시지에 클래스 이름이 안 나오던 것까지 고쳤다(CPython은 자기 `tp_name`).
- arity 문형도 함께 잡았다: SharpPy는 `X expected at most 1 arguments (2 given)`이었고 CPython은
  `X expected at most 1 argument, got 2` (`bool`/`list`/`tuple`/`dict`/`set`/`frozenset`/`dir`).

### 회귀·성능

- `tests/bughunt/p62_constructor_keywords.py` **51케이스 diff 0** (str/bytes/text 표 + tuple.__new__
  + arity 문형 포함). bughunt **67/67**, evaluate 불변 `225 / 3 / 2`, Godot 4.7.2 mono replay
  `82 matched, 0 other`.
- `perf_probe`가 `str_fmt +30.6%` / `list_comp +20.9%`로 플래그했고 실제로 내가 건드린 경로와
  겹쳤지만, 9라운드 교대 A/B(`6a95c68` vs HEAD)에서 `str_fmt +1.7%`, `list_comp +3.0%`,
  **TOTAL -0.9%**였다. old가 new보다 빠른 라운드가 섞이고 `list_comp`는 양쪽 모두
  0.058~0.107의 bimodal 분포 — 회귀가 아니다. 이 항목은 hot path이 아니라 생성자 진입부이므로
  메커니즘도 성능을 지지하지 않는다.

## P0-26 (2026-10-30 **닫힘**): builtin 함수의 keyword 시그니처

P0-20은 *descriptor*(메서드)의 keyword 표를 고쳤고 P0-25는 *생성자*를 고쳤다. 모듈 레벨 builtin
함수는 여전히 CPython이 받는 것을 거부하고, 거부하지 않을 때는 **keyword를 버린 뒤 그 keyword가
채울 인수가 없다고 불평**했다.

```text
abs(x=-5)          CPython: abs() takes no keyword arguments
                   이전:    abs() takes exactly one argument (0 given)
hasattr(obj=1, ...) CPython: hasattr() takes no keyword arguments
                   이전:    hasattr() takes exactly 2 arguments (0 given)
```

거부해야 할 호출을 다른 호출로 바꿔 보고 있었으므로 메시지 문제가 아니라 실행 문제였다.

### 표는 생성했다

`__text_signature__`에서 인수 이름을 읽어 케이스를 만들었다(`.artifacts/tmp/gen_builtin_kw_table.py`,
결과 JSON `.artifacts/tmp/builtin_kw_table.json`). 추측 금지 규칙을 지키려고 표를 손으로 쓰지 않았다.
규칙: 첫 번째 맨 `/` 앞은 positional-only, 그리고 `/`가 뒤에 붙은 인수는 그 뒤에라도
positional-only다. `sorted(dict, key=len)`은 되고 `sorted(iterable=[1])`는 안 되는 이유가 후자다.

- **keyword를 전혀 받지 않는 32개** — abs, aiter, all, anext, any, ascii, bin, callable, chr,
  delattr, dir, divmod, eval, format, getattr, globals, hasattr, hash, hex, id, input, isinstance,
  issubclass, iter, len, locals, next, oct, ord, repr, setattr, vars. 전부 같은 문구
  `name() takes no keyword arguments` → `PyBuiltinFunction.Call`에 중앙 게이트.
- **이름을 받는 것들** — `round(number, ndigits)`, `pow(base, exp, mod)`,
  `print(*args, sep, end, file, flush)`, `sorted(*, key, reverse)`, `sum(iterable, start)`,
  `min/max(*, key, default)`, `enumerate(iterable, start)`, `zip(*iterables, strict)`과
  `exec(*, closure)`, `compile(...)`, `__import__(name, ...)`. `round`/`pow`/`sum`/`enumerate`는
  이름 인수를 **바인딩하지 않았다**(`pow(2, 3, mod=5)`가 `mod`를 무시하고 8을 줬다).

### 측정으로 드러난 함수별 차이

| 사실 | 근거 |
|---|---|
| `sorted`의 모른 이름 메시지는 `sort()` | `sorted([1], zzz=1)` → `'zzz' is an invalid keyword argument for sort()` — list.sort와 파서를 공유 |
| arity가 keyword 검사보다 먼저인 함수가 있다 | `min(zzz=1)` → `min expected at least 1 argument, got 0` |
| 반대 순서인 함수가 있다 | `enumerate(zzz=1)` → `'zzz' is an invalid keyword argument for enumerate()` |
| 필수 인수가 없으면 위치를 찍는다 | `pow()` → `pow() missing required argument 'base' (pos 1)`, `round()` → `...'number' (pos 1)` |
| `enumerate`는 positional-only가 아니다 | `list(enumerate(iterable=[7])) == [(0, 7)]` — ledger에 있던 "`enumerate(iterable=…)`가 동작해야 한다"는 맞았지만 이유 달성이 달랐다 |
| `reversed`는 positional-only다 | `reversed(sequence=[1])` → `reversed() takes no keyword arguments` |
| `map`/`filter`/`range`는 CPython도 거부 | `map(function=None, …)` → `map() takes no keyword arguments` |

### 회귀·성능

- `tests/bughunt/p66_builtin_keywords.py` — 생성 표 63케이스 + 이름 인수/모른 이름/순서 35케이스,
  차이 0.
- bughunt **71/71**, evaluate 불변 (`225 / 3 / 2`).
- Godot 4.7.2 mono replay `82 matched, 0 other`, 오류 0개 빌드.
- `tools/perf_probe.py` 지적 없음(모두 +0~9% 대역, 이전 기록의 노이즈 대역 안). 게이트는
  `kwargs != null`일 때만 동작하고, 이름 인수를 읽는 추가는 kwargs가 있는 호출만 탄다.

## P0-29 (2026-10-30 등록, attribute 계열은 닫힘): positional 과잉 인수의 **문구 계열**

P0-26이 keyword면을 정리하고 남긴 것은 위치 인수 문구다. 표본 15개 중 **11개**가 다르다(측정):

| 표현 | CPython | SharpPy |
|---|---|---|
| `divmod(7)` | `divmod expected 2 arguments, got 1` | `divmod expected exactly 2 arguments (1 given)` |
| `reversed([1], 2)` | `reversed expected 1 argument, got 2` | (p66에서 고정) |
| `next(it, 2, 3)` | `next expected at most 2 arguments, got 3` | `next expected 1 or 2 arguments (3 given)` |
| `id()` | `id() takes exactly one argument (0 given)` | `id expected exactly 1 arguments (0 given)` |
| `chr(65, 66)` | `chr() takes exactly one argument (2 given)` | `chr expected exactly 1 arguments (2 given)` |
| `isinstance(1)` | `isinstance expected 2 arguments, got 1` | `isinstance() takes exactly 2 arguments (1 given)` |
| `getattr(1)` | `getattr expected at least 2 arguments, got 1` | `getattr expected 2 or 3 arguments (1 given)` |
| `format(1, "", "")` | `format expected at most 2 arguments, got 3` | `format() takes 1 or 2 arguments (3 given)` |
| `any()` | `any() takes exactly one argument (0 given)` | `any expected exactly 1 arguments (0 given)` |
| `enumerate()` | `enumerate() missing required argument 'iterable'` | (p66에서 고정) |

CPython에는 세 계열이 공존한다: clinic의 `X() missing required argument 'k' (pos N)`,
generic의 `X() takes exactly one argument (N given)`, 그리고 직접 작성된
`X expected [at least|at most] N argument[s], got M`. SharpPy는 `{name} expected exactly N arguments (M given)`
같은 **혼합형**을 만들어 어느 계열에도 속하지 않는다. 함수마다 어느 계열인가를 측정 표로 두고
한 번에 정리할 것 — p66이 `reversed`/`enumerate`/`pow`/`round`/`sorted`/`sum`은 이미 고정했으므로,
남은 것은 나머지 함수들이다.

### 같은 항목의 두 번째 부분: getattr/hasattr가 사용자 예외를 삼킨다

```python
class Boom:
    def __getattr__(self, name):
        raise ValueError("boom")
```

| 표현 | CPython | SharpPy |
|---|---|---|
| `getattr(Boom(), "anything", "D")` | `ValueError: boom` | `'D'` |
| `hasattr(Boom(), "anything")` | `ValueError: boom` | `False` |

CPython의 `getattr` 기본값은 **AttributeError만** 잡는다. SharpPy는 `BuiltinsModule.GetAttr`가
`catch { return defaultValue; }`이고 `CallHasAttr`도 `AttributeError`만 잡는 경로와 이중으로
 존재한다. 또한 `getattr` 구현이 두 개다 — `modules/BuiltinsModule.cs`와 `core/PyBuiltin.cs:CallGetAttr`
 — 그리고 `CallGetAttr`에는 `result is PyNone && default != null → 기본값`이라는 별도 우회가 있어
 `x = None`인 속성을 잘못 삼킬 수 있다(측정으로는 module 쪽이 이기고 있어 지금은 증상이 안 난다).
 `abs`에서 했던 것처럼 구현을 하나로 합치고 `AttributeError`만 잡도록 고친다.


### 닫힌 부분 (p67): getattr/hasattr/setattr/delattr

- 기본값을 받는 `getattr`과 `hasattr`는 **AttributeError만**suppress한다. BuiltinsModule에 있던
  두 번째 구현이 모든 예외를 잡아서 `getattr(Boom(), "anything", "D")`가 `'D'`를 반환했다. 이제 구현은
  하나다. `x = None`인 속성을 없는 속성으로 취급하던 분기도 제거했다.
- 이름 인수 메시지는 네 함수 모두 CPython과 같아졌다: `attribute name must be string, not 'int'`.
- 위치 인수 문구도 측정한 대로 고쳤다 (`hasattr expected 2 arguments, got 3`,
  `getattr expected at least 2 arguments, got 1`).
- 삭제 메시지는 CPython에서 네 갈래다 (descrobject.c getset / object.c read-only / dict 경로 /
  `object_set_class`)라는 것을 소스에서 확인하고 그대로 옮겼다. `gate`는 객체가 인스턴스 dict를
  가지는지, 이름이 어떤 슬롯에서 resolution되었는지로 결정된다.
- 남은 것은 attribute 계열이 아닌 나머지 builtin의 위치 인수 문구다.

## P0-30 (2026-10-30 등록): 숫자 타입의 속성 표면 - bool이 int 디스크립터를 미치고, real/__getnewargs__가 보이지 않는다

구조 검사가 내보낸 두 계열.

| 표현 | CPython | SharpPy |
|---|---|---|
| `(True).real` | `1` | `TypeError: descriptor 'real' for 'int' objects doesn't apply to ...` |
| `(True).imag`, `(True).conjugate()` | `0`, `1` | 같은 `TypeError` |
| `hasattr(1, "__getnewargs__")` | `True` | `False` |
| `hasattr(1.5, "__getnewargs__")` | `True` | `False` |
| `hasattr(1+2j, "__getnewargs__")` | `True` | `False` |
| `setattr(1.5, "real", 2)` | `AttributeError: attribute 'real' of 'float' objects is not writable` | `AttributeError: 'float' object has no attribute 'real'` |

`bool`은 `int`의 하위 타입이므로 CPython에서 int 슬롯은 `PyLong_Check` 통과 경로다. 지금은 정확히 타입을
비교해서 거부하고, 거기다 그 메시지 자체가 CPython에 없는 형태이며 큰따옴표까지 붙어서 나온다.
`__getnewargs__`는 pickle 협정 표면이고 P0-28의 남은 가정 목록에 이미 같은 종류로 들어 있다.

## P0-31 (2026-10-30 등록): `str.upper()` / `str.lower()`가 ASCII 이상에서 아무 반응도 없다

| 표현 | CPython | SharpPy |
|---|---|---|
| `"ß".upper()` | `'SS'` | `'ß'` 그대로 |
| `"İ".lower()` | `'i\u0307'` | `'İ'` 그대로 |

`casefold`(`'ß' → 'ss'`), `title`, `swapcase`, `istitle`은 정확하므로 문제는 표가 아니라 이 두 메서드가
단순화된 처리로 돌아간다는 것이다. CPython은 Unicode 기준 대소문자 운영(`unicode_upper`/`unicode_lower`,
최대 확장 길이 3까지)을 쓴다. 증상이 안 보이던 이유는 p02 검사가 ASCII 기준이었기 때문이다. p02를
비-ASCII 검사로 확장하고, culture 영향이 없는 경로로 바꾼다.

## P0-32 (2026-10-30 등록): `float.fromhex`가 예외 없이 0.0을 반환한다

`float.fromhex("0x1p3")`은 CPython `8.0`, SharpPy `0.0`. 조용히 틀리는 것이 최악이다. CPython의
`float_fromhex_core`가 받는 문법(16진 가수·지수, `inf`/`nan`, 공백, 부호)을 기준으로 다시 쓴다.

## P1 모음 (2026-10-30 검사): 같은 것을 두 번 구현한 자리

1. **builtin 표 항목과 모듈 delegate가 둘 다 있는 것 13개** — `abs, ascii, bin, compile, format, hex,
   isinstance, issubclass, len, memoryview, oct, pow, vars`. 뒤에 등록된 delegate가 이기므로
   `core/PyBuiltin.cs`의 표 항목은 실행되지 않는 코드다. 하나로 합친다.
2. **`operator.pow`/`operator.abs`가 `builtins`와 별도 구현** — CPython이 거부하는 keyword까지 받고,
   라벨에 `_operator.` 접두가 없다 (CPython `_operator.pow() takes no keyword arguments`). `abs`에서
   한 구현으로 모았을 때 따랐던 길을 다시 벗어났다.
3. **`set.copy`**가 `TypeDict`와 `GetAttribute` 특별 분기 두 곳에 등록되어 있다.
4. **호출자가 없는 구 구현들** — `PyString.Upper()`, `PyString.Lower()`, `PyString.Capitalize()`는
   호출자가 없고, `Capitalize`는 `CurrentCulture.TextInfo.ToTitleCase`를 쓴다. 살아있는 `title`이
   CPython 단어 경계 규칙으로 의도적으로 바꾸어 놓았다는 것을 모르는 사람이 저 전화를 고치면 같은
   오류가 되산다. 삭제한다.
5. **예외 매칭이 두 갈래** — `runtime/PyVM.cs:7025`가 타입 이름을 `StringComparison.OrdinalIgnoreCase`로
   비교하고 `"Exception"` 하드코딩까지 둔다. CPython의 매칭은 클래스 객체/MRO 기준이다. p07이 달성은
   뒤에 MRO 경로가 먼저 이기기 때문이고, 이 이름 비교 경로 자체는 유효하다. 삭제하고 MRO 판정 하나로
   합친다.
6. **codec 호출이 표 밖에서 4개 파일** — `modules/_CodecsModule.cs`(4), `runtime/PyBinaryTypes.cs`(3),
   `core/PyFileContextManager.cs`(1)가 `Encoding.GetEncoding`을 직접 불러서 `core/PyString.cs`의
   `NormalizeCodecName` 별칭 표와 다른 경로로 간다. 모두 그 함수로 모은다.
7. 인수 검사 문자열이 8계열 400개 넘게 흩어져 있다. 계량은 P0-29로 남긴다.

## P1 (2026-10-30 등록): codec 표의 범위와 strict 디코딩 한계

`str(bytes, encoding)` / `bytes(str, encoding)`을 C#으로 옮기면서 남긴 경계. 정직하게 남긴다:

- CPython은 수백 개 alias를 `_codec_encodings` 테이블로 받는다. SharpPy는 문서/테스트에 나오는
  집합(utf-8/latin-1/ascii/utf-16*/utf-32*/big5/gbk/shift_jis/koi8-r/cp125x)만 명시 매핑하고
  나머지는 .NET `CodePagesEncodingProvider`에 위임한다. 따라서 CPython이 받는 마이너 alias가
  `LookupError: unknown encoding: X`로 나올 수 있다.
- **strict 모드는 UTF-8만 정확하다.** .NET의 `GetString`은 invalid input을 조용히 치환하므로
  strict에는 throw하는 encoding이 필요한데, `UTF8Encoding(false, true)`만 그렇게 만들었다.
  다른 codec에서는 invalid byte가 `UnicodeDecodeError` 대신 U+FFFD가 된다.
- `errors`는 strict/ignore/replace만 안다. CPython의 `surrogateescape`, `backslashreplace`,
  `xmlcharrefreplace`, `namereplace`는 미구현(`LookupError`로 거절하는 쪽을 택했다 — 조용한 오답보다
  낫다).
- `bytes(str, encoding)`의 `UnicodeEncodeError` 메시지는 위치/사유를 축약했다
  (`'X' codec can't encode character: unencodable text`). CPython은 문자·위치·사유를 모두 본다.


## P0-24 항목 4 / P0-27 (2026-10-30 **닫힘**): `class B(bytes)`가 subclass identity를 잃던 것

측정 (`p63`, 58케이스)으로 확인된 원인은 세 겹이었다.

1. **생성 경로가 subclass를 지운다.** `PyClass.Call`이 MRO에 bytes를 보는 즉시
   `return PyBuiltinFunction.CallBytes(args, kwargs);` — CPython은 `bytes_new`가 **요청된 타입을
   존중**해서 `bytes_subtype_new`로 값 먼저 만들고 subclass로 할당한다. SharpPy는 plain bytes를
   반환하므로 `type(B(b"xy")).__name__`이 `'bytes'`였고, 선언한 `__len__`/`__add__`/`__contains__`/
   `__iter__`/`__getitem__`/`__eq__`/`__hash__`/`__repr__`가 **모두** 도달 불가였다.
2. **`bytes.__new__`가 아예 등록되어 있지 않다.** attribute lookup이 generic fallback으로 떨어져
   `bytes.__new__(cls, value)`가 `type.__new__`로 해석됐다 → user `__new__`에서 위임하는 보통의
   불변 byte 래퍼가 `TypeError: type.__new__() takes exactly 4 arguments`로 죽었다. 이제
   `bytes.__new__(B, b"zz")`는 B를 만들고, `bytes.__new__(5, …)`는 CPython 문구
   `bytes.__new__(X): X is not a type object (int)`를 낸다.
3. **bytes에 sequence slot wrapper가 없다.** CPython `bytes.__dict__`에는 `__add__`/`__mul__`/
   `__rmul__`가 있고 **`__radd__`는 없다**(sq_concat이 C에서 양쪽을 처리). SharpPy는 셋 다 없어서
   `3 * B(b"a")`가 `unsupported operand type(s) for *`였다. wrapper_descriptor로 등록했고 메시지
   형식도 CPython slot wrapper다(`wrapper __mul__() takes no keyword arguments`,
   `expected 1 argument, got 2`).

### 함정: `LookupUserSpecial`은 클래스 객체에게 다른 답을 준다

gate를 "`__new__`를 user가 선언했는가"로 만들 때 `LookupUserSpecial("__new__")`를 썼더니
`class B(bytes)`에서도 non-null이 나왔다. 그 헬퍼는 `GetPyType().MRO`를 walk하는데, **클래스 객체의
`GetPyType()`은 metaclass**다. 즉 "이 클래스가 `__new__`를 선언했나"가 아니라 "type이 `__new__`를
선언했나"를 답하고 있었다. worktree 계측으로 확인:

```
DBG CI B derives=True userNew=PyStaticBuiltinMethod getPyType=type newMethod=PyNewMethodDescriptor mro=B,bytes,object
```

그래서 `PyClass.DeclaresUserSpecial(name)`을 넣었다 — **자기 자신의 MRO**에서 PyClass frame만 보고
non-PyClass에서 멈춘다. 인스턴스용 `LookupUserSpecial`과 클래스용 `DeclaresUserSpecial`은 서로 다른
질문이며, 이 구별을 코드에 명시했다.

### 남은 중복 (정직하게)

생성 진입점이 두 개다: `PyType.CreateInstance`(virtual)과 `PyClass.CreateInstance`(`public new` —
**shadowing이라 virtual dispatch가 base를 부른다**). 그래서 `B(b"x")`는 메타클래스 경유 여부에 따라
다른 메소드로 들어간다. bytes는 두 쪽 모두에 gate를 걸었지만, 이 shadowing 자체는remove해야 할
중복 경로다. 지금도 `PyType.Call → CreateInstance`는 base 구현을 부르고, `PyTypeMetaclass.cs:211`의
`pyClass.CreateInstance(args)`는 파생 구현을 부른다. 한 곳으로 합치는 작업이 남았다.

### 회귀·성능

- `tests/bughunt/p63_bytes_subclass.py` 58케이스 diff 0. bughunt **68/68**, evaluate 불변.
- Godot 4.7.2 mono replay `82 matched, 0 other`.
- A/B(9라운드, `2ef7b82` vs 이 변경): `str_fmt +3.5%`, `list_comp +1.9%`, **TOTAL +1.1%** — 이전에
  same-flag로 노이즈로 판정된 대역 안. bytes subclass 생성 외 경로이고, 신규 코드는 생성 진입부와
  타입 등록 시점에만 돈다.


---

## P0-14 (2026-10-30 **닫힘**): bytearray은 bytes 메서드 표면을 위임으로 가져온다

`bytearray(b"ab").upper()`가 `AttributeError`였다. p40/p41이 search/query 계열(`find`, `count`,
`index`, `startswith` 등)만 다뤘고 `upper`, `strip`, `split`, `replace`, `hex`, `join`, `is*` 계열
32개는 `bytearray`에 아예 존재하지 않았다. CPython은 두 타입이 같은 로직을 갖고 있으나 결과를
자기 타입으로 되돌려 주고, `split`·`rsplit`·`splitlines`·`partition`·`rpartition`·`join`은 결과
안의 **원소까지** `bytearray`로 만든다.

### 구현: 두 번째 구현을 쓰지 않는다

bytes 메서드가 두 번 등록되는 형태는 이 프로젝트가 받아들이지 않는다. 위임 구조는 이렇게: bytearray
메서드는 자기 바이트 배열로 같은 bytes 프록시를 만들어 bytes가 등록한 디스크립터에
`CallWithReceiver`로 건네고, 되돌아온 `bytes`를 `bytearray`로 바꾸며 결과 목록/튜플 안 원소까지
재귀 변환한다. 그래서 `bytes.upper`를 고치면 `bytearray.upper`도 함께 고쳐진다. 예외가 하나 있는데,
`maketrans`는 CPython도 `bytes`를 되돌려 주므로 변환하지 않는다 (`fromhex`는 `bytearray`).

### 위임이 드러낸 bytes 쪽 결함 (P0-28이 아닌 별개 건들)

| 건 | 전 | 후 (CPython과 동일) |
|---|---|---|
| `bytes.hex(sep)` | 인수를 무시 | `'01:ab'`, `sep` 길이가 1이 아니면 `sep must be length 1.`, buffer가 아니면 `object of type 'int' has no len()` |
| `bytes.join` | `PyList` 안의 `PyBytes`만 | 임의 iterable 허용. 원소는 bytes/bytearray/memoryview만 (`str`이면 `sequence item 0: expected a bytes-like object, str found`), iterable이 아니면 `can only join an iterable` |
| `bytes.split` / `rsplit` | kwargs 무시, 세 번째 위치 인수도 무시 | `sep`/`maxsplit` 이름 허용, `split() takes at most 2 arguments (3 given)` |
| `bytes.expandtabs` | kwargs 무시 | `tabsize=` 허용 |
| `bytes.fromhex` | 공백 제거 후 단순 채번, 위치 없는 메시지 | CPython처럼 원문 인덱스를 쫓는 조회. `non-hexadecimal number found in fromhex() arg at position 3` (`'abc'`는 반 조각이 남으면 문자열 끝 위치) |
| `bytes.maketrans` | `maketrans() takes exactly 2 arguments (1 given)` | `maketrans expected 2 arguments, got 1` |

`bytes.hex`는 구분자가 `str`도 받는 예외다(`hex(":")` 동작). `split`은 `str`을 거절한다
(`a bytes-like object is required, not 'str'`). CPython이 그렇게 다르므로 두 포맷을 통합하지 않았다.

### 함정 두 가지

1. **classmethod 바인딩이 위치 인수를 삼킨다.** `bytearray.fromhex`를
   `PyClassMethodDescriptor`로 등록하면 `Get()`이 `PyCMethod`를 만들면서 클래스를 첫 위치 인수로
   넣는다. 위임된 bytes 구현은 이를 자기 인수로 계산해서
   `fromhex() takes exactly one argument (2 given)`을 낸다. 결과형과 메시지형이 다 어긋난다.
   해결은 일반 함수로 등록하고, 클래스 접두가 필요한 검사만 위임 전에 책임지는 방식(아래 2항)이다.
   CPython도 `type(bytearray.fromhex)`가 `builtin_function_or_method`라 관측 차이는 없다.
2. **일반 과잉 인자 메시지는 정의 타입으로 접두가 붙는다.** CPython의 일반 인수 파서는
   `bytearray.upper() takes no arguments (1 given)`처럼 풀네임을 찍고, 직접 작성된 파서는
   `strip expected at most 1 argument, got 2`처럼 고유 문구를 쓴다. 위임하면 `bytes.` 접두가
   나오는데 후자는 그대로 나와야 한다. 그래서 위임 래퍼는 측정으로 만든 표(QualifiedArity /
   NamedKeywordMethods)로 접두 계열만 자체 생성하고 나머지는 bytes 구현에 그대로 맡긴다.
   `bytearray.decode(encoding=...)`를 위임 래퍼의 키워드 거부로 막으면 안 되고
   `bytearray.upper(x=1)`은 막아야 한다는, 반대 방향 요구가 동시에 성립해야 하는 자리다.

### 회귀·성능

- `tests/bughunt/p64_bytearray_bytes_backed.py` — 값/결과형/클래스 함수/오류 메시지 포함 차이 0.
- bughunt **70/70**, evaluate 불변 (`225 / 3 / 2`).
- Godot 4.7.2 mono: 소스 포함 Release 빌드 → replay `82 matched, 0 other`, 기본 프로브
  `SHARPPY_GODOT_PASS` (호스트 .NET 10.0.12).
- `tools/perf_probe.py`는 3번 실행에서 `str_fmt +23.1%`, `list_comp +21.8%`를 지적했다. 같은
  바이너리로 뒤이은 9라운드 교차 A/B(`c260a27` vs 이 변경)에서는 `str_fmt -2.3%`,
  `list_comp -6.9%`, **TOTAL -0.8%**. 두 바이너리 모두 `list_comp`가 0.053–0.122 사이 두 봉우리
  분포 → 노이즈.

## P0-28 (2026-10-30 등록): 내장 타입 표면이 **어떤 객체를 먼저 만들었는지**에 걸려 있었다

P0-14를 위임 구조로 바꾸던 중에 나온 결함이다. 모든 내장 타입은 자기 TypeDict 항목을 CLR **정적
생성자**에서 붙이는데, 그 시점은 Python 타입을 처음 쓰는 시점이 아니라 해당 CLR 클래스를 처음
건드리는 시점이다. 두 시점이 어긋난다.

측정 결과 (bytes 객체를 하나도 만들지 않은 스크립트):

```text
bytes.TypeDict 항목 수     1  (같은 런타임의 str 63, list 33, int 57)
dir(bytes)                 24 (CPython 78)
hasattr(bytes, "upper")    False
getattr(bytes, "fromhex")  AttributeError: type object 'bytes' has no attribute 'fromhex'
b"a".upper()               'AB'   (정상)
```

성질 자체가 두 갈래라 강하다. 호출은 같은 디스크립터를 타는데 `hasattr`/`getattr`/`dir`/ 타입
사전/ABC 구조 검사만 못 본다. 이전에 `collections.abc` 구조 검사를 네이티브 능력 프리미티브로
처리해야 했던 것도 같은 무릎을 판다.

### 고친 부분

`PyType` 정적 생성자가 `RuntimeHelpers.RunClassConstructor`로 관련 정적 생성자들을 강제 초기화한다.
돌아가야 하는 순서가 있다: `str`과 숫자 타입이 먼저(코덱과 에러 문구가 이들을 쓴다), 그다음 `bytes`,
`bytes` 다음 `bytearray`(bytearray의 위임 표면이 bytes의 TypeDict에서 복사된다), 사용자 클래스를
세우는 `PyClass`가 맨 뒤. 효과: `dir(bytes)` 24 → 70, `bytearray` → 80,
`hasattr(bytes, "upper")` True, `getattr(bytes, "fromhex")` → `builtin_function_or_method`.

### 남은 부분 (이 항목이 열려 있는 이유)

정적 생성자는 있는데 TypeDict를 채우지 않는 타입들이 남았다. 모두 측정했다.

| 검사 | CPython | SharpPy |
|---|---|---|
| `hasattr(frozenset, "union")` | True | **False** |
| `hasattr(complex, "conjugate")` | True | **False** (`dir(complex)` 24) |
| `hasattr(memoryview, "cast")` | True | **False** (`dir(memoryview)` 24) |
| `"__getnewargs__" in dir(bytes)` | True | **False** |
| `hasattr(type({}.keys()), "isdisjoint")` | True | **False** |
| `hasattr(type(iter([])), "__length_hint__")` | True | **False** |

동작 자체는 같은 경로로 된다(`frozenset([1]).union({2})`, `complex(1,2).conjugate()`,
`memoryview(b"ab").cast("B")` 모두 정확). 디스패치는 맞고 검사 API만 틀린 상태다. `dir()` 내용
부족(기존 P1)과 같은 단계에서 합쳐 처리할 가능성이 있다.

### 가드

`tests/bughunt/p65_registration_order.py` — 객체를 만들기 전에 타입 표면을 읽는 순서를 고정한다.
지금 맞는 부분까지 검증하고, 위 남은 6건은 이 항목에 문자로 남기되 프로브가 같이 무너지지는
않도록 넣지 않았다.
