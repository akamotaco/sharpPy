# SharpPy — Known CPython-parity gaps (consolidated ledger)

Maintained in one place. Per-cluster findings live in `docs/reports/<date>/pNN_*.md`;
this file is the live backlog only. Add an entry when a probe proves a divergence,
remove it (or move to a "fixed" note) when a cluster closes it.

Reference interpreter: **CPython 3.12.15**
(`C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe`).

## Verification snapshot (this state)

- `tools/evaluate.py`: `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- `tools/run_bughunt.py`: **52/52 matching**
- Every open P0 below was **re-measured 2026-10-29** (`.artifacts/tmp/p0rest.py`), so none
  of them is a stale entry. Byte argcheck (old P0-3) closed via p38/p40/p41 and is gone.
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

| P0-14 | bytearray surface | 33 methods still missing on `bytearray`: `capitalize center decode expandtabs fromhex hex isalnum isalpha isascii isdigit islower isspace istitle isupper join ljust lower lstrip maketrans partition replace rjust rpartition rsplit rstrip split splitlines strip swapcase title upper zfill` (CPython has all; only `isprintable` legitimately absent) | p40 measured |

| P0-5 | static `type` | `type.foo = 1` succeeds; CPython → `TypeError: cannot set 'foo' attribute of immutable type 'type'` (SharpPy `type` is a heap `PyClass`) | p28 |

| P0-8 | `io.TextIOWrapper(buffer, encoding=…)` | wrapping an existing stream with an encoding not supported | io follow-up |
| P0-9 | importlib semantics | custom loader/spec beyond `__file__`; namespace-package behavior | p15/p18 follow-up |
| P0-16 (partial, p48) | `float` subclass payload | `class FSub(float)` instances carry no float value: `repr(FSub(2.5))` → `<__main__.FSub object at 0x…>` (CPython `'2.5'`), `FSub(2.5) + 1` → `TypeError: unsupported operand type(s) for +: 'FSub' and 'int'`, `FSub(2.5) == 2.5` → `False`, `float(FSub(2.5))` → `TypeError`. `int`/`str` subclasses work because `PyIntSubclass`/`PyStrSubclass` exist; there is no `PyFloatSubclass` | p44 measured |

Suggested order (risk × effort, cheapest real-computation wins first):
**P0-16 residuals** (reflected ops / `abs` / float descriptor self-check / hash) →
**P0-14** (33 bytearray methods, mechanical families) → P0-8 / P0-9 (each needs a real
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
  surface를 C# 특징을 고려해 구현하는 방향으로 확정했다. 구성実측: `Lib` 41개 파일 18,922줄, native C#
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
