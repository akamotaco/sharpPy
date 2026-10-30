# SharpPy — Known CPython-parity gaps (consolidated ledger)

Maintained in one place. Per-cluster findings live in `docs/reports/<date>/pNN_*.md`;
this file is the live backlog only. Add an entry when a probe proves a divergence,
remove it (or move to a "fixed" note) when a cluster closes it.

Reference interpreter: **CPython 3.12.15**
(`C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe`).

## Verification snapshot (this state)

- `tools/evaluate.py`: `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- `tools/run_bughunt.py`: **86/86 matching** (p76 150 lines, p78, p79, p80, p81 added; all diff 0)
- `tools/cpython_similarity.py`: **0 broken C anchors**, 17 mechanism rows all cited from C#
- `tools/argv_matrix.py`: **0 mismatches** (P0-41로 등록된 gap은 제외)
- P0-14 closed by p64. The registration-order fix P0-14's delegation depends on is
  guarded by p65; the surface gap it exposed for other builtin types is **P0-28**.
- Every open P0 below was **re-measured 2026-10-29** (`.artifacts/tmp/p0rest.py`), so none
  of them is a stale entry. Byte argcheck (old P0-3) closed via p38/p40/p41 and is gone.
- 2026-10-30 closings after that snapshot: P0-21 (mostly), P0-23, P0-24 items 1-4, P0-22,
  **P0-25** (builtin constructor keyword signatures), **P0-27** (bytes subclasses). The table
  above still shows P0-16 as partial; p48 plus the P0-18/P0-23 sequence closed it.
- 2026-10-30 **P0-34 cohort 2** (`type/PyString.cs` code-point coordinates, p75): len, indexing,
  slicing, iteration, `reversed`, `find`/`index`/`rfind`/`rindex`, `count`, `startswith`/`endswith`,
  `ljust`/`rjust`/`center`/`zfill`, the format mini-language, text ordering, `ascii()` and the
  character class tests now all count **characters**. One conversion point replaced four copies of
  the find/index arithmetic and four dead predicate methods; repository `Value.Length` sites went
  from 153 to 107 (`type/PyString.cs` 70 to 26). `bytes.center` also stopped rounding the extra byte
  to the wrong side. What is left is listed under **P0-34 remainder** below.
- 2026-10-30 **P0-33** (`core/PyExceptionGroup.cs` rewritten from `Objects/exceptions.c`, p74):
  exception groups stop losing their members. `repr`/`.message`/`.args`/`split`/`subgroup`/
  `derive` and the constructor error family now match CPython, the VM calls `exc.split(...)` like
  `exception_group_match` in `Python/ceval.c` instead of keeping its own matcher, and
  `_PyExc_PrepReraiseStar` is transliterated. Every uncaught exception also stopped printing a
  second `--- C# Exception Details ---` block on stdout. What is left is listed under
  **P0-33 remainder** below.
- 2026-10-30 codec registry (`core/PyCodecRegistry.cs`, p73): `codecs.lookup` /
  `getencoder` / `getdecoder` / `register` / `unregister`, CPython name normalisation and
  canonical names, `rot-13` / `hex` / `base64` as real non-text codecs, and the
  "is not a text encoding" refusal family. The seventh copy of the decoder
  (`_codecs.*_decode` calling `Encoding.GetString`) is gone; those entry points now ask
  `DecodeBytes`. Open remainder is listed under **P1 codec registry follow-ups** below.
- 2026-10-30 **P0-37** (`modules/GcModule.cs`, `core/PyFinalizer.cs`, p76): `import gc` stops raising
  `ModuleNotFoundError`. `gc` is a built-in C module in CPython, so it is implemented rather than
  copied: surface, `gc.`-prefixed refusals, threshold/debug/enable state, real `callbacks` that
  `collect()` calls with a `{collected, generation, uncollectable}` dict, `garbage`, `get_stats`,
  the measured `is_tracked` rule and `get_referents` traverse order. `__del__` is now dispatched as
  a special method (subclass override, `super().__del__()`, inherited hook) and runs at interpreter
  shutdown in creation order, with CPython's `Exception ignored in:` report on stderr. What cannot
  match while lifetime belongs to the CLR collector is listed under **P0-37 remainder** below.
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

네 번째 계열이 추가됐다: method descriptor의 과잉 인수 문구는 정의 타입을 붙인다 (`int.bit_length() takes no arguments (1 given)`), slot wrapper는 `expected 0 arguments, got 1`을 쓴다. p68이 numeric 계열 파일을 고정했고, 나머지는 아직이다.

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

## P0-30 (2026-10-30 **닫힘**): 숫자 타입의 속성 표면 - bool은 int이고, 값은 receiver가 아니다

구조 검사가 내보낸 두 계열.

| 표현 | CPython | SharpPy |
|---|---|---|
| `(True).real` | `1` | `TypeError: descriptor 'real' for 'int' objects doesn't apply to ...` |
| `(True).imag`, `(True).conjugate()` | `0`, `1` | 같은 `TypeError` |
| `hasattr(1, "__getnewargs__")` | `True` | `False` |
| `hasattr(1.5, "__getnewargs__")` | `True` | `False` |
| `hasattr(1+2j, "__getnewargs__")` | `True` | `False` |
| `setattr(1.5, "real", 2)` | `AttributeError: attribute 'real' of 'float' objects is not writable` | `AttributeError: 'float' object has no attribute 'real'` |

`bool`은 `int`의 하위 타입이므로 CPython에서 int 슬롯은 `PyLong_Check` 통과 경로다. CLR이 bool을 `PyInt`로
모델링하지 않기 때문에 그 검사 지점마다 bool을 명시해야 한다 (`TryLongSelf`).

측정해서 고친 것 (p68, 46케이스 차이 0):

| 규칙 | CPython 근거 | 고친 곳 |
|---|---|---|
| `real`/`numerator`/`conjugate`는 receiver가 아니라 `long_long` | longobject.c:5358-5367 - 정확히 int만 같은 객체, bool과 int 하위 타입은 평범한 int 사본 | `type(I(3).real)`이 `int`가 됐다 |
| `float.real`/`conjugate`는 항상 새 `PyFloat` | floatobject.c `float_getreal`이 `PyFloat_FromDouble` | `type(F(2.5).real)`이 `float` |
| `__getnewargs__`는 타입 dict에 공개된 메서드 | longobject.c:5679 / floatobject.c:1714 / complexobject.c:653 | int/float/complex 모두 등록 |
| 대입은 **합쳐진** 타입 dict를 본다 (`_PyType_Lookup`) | object.c:1560-1599 | `PySetAttribute`가 PyClass 프레임만 봐서 float의 getset을 못 찾았다 |
| unbound self 검사는 하나의 문구 | descrobject.c descr_check | int/float/complex의 제각각 문구를 통일 |
| method descriptor 과잉 인수 문구는 정의 타입을 붙인다 | `int.bit_length() takes no arguments (1 given)` | bit_length/bit_count/as_integer_ratio/is_integer/hex/fromhex/conjugate/__getnewargs__/complex.__format__ |
| slot wrapper는 다른 문구 | `expected 0 arguments, got 1` | `float.__repr__` |

남은 두 가지는 이 항목이 아니라 타입 표면 항목이다 (P0-28/P1로 옮긴다):

- `int.__dict__["real"].__get__(5)` — CPython은 `5`를 주지만 SharpPy의 내장 디스크립터 객체에는
  `__get__`/`__set__`/`__delete__`가 없다.
- `type(int.__dict__["real"]).__name__` — CPython `getset_descriptor`, SharpPy `property`.

그리고 위의 **정의 타입 접두 문구 계열**은 numeric 파일 밖에도 넓게 퍼져 있다. `bytearray`에서 이미
확인했고 (`bytearray.upper() takes no arguments (1 given)`), P0-29의 한 갈래로 센다.

## P0-31 (2026-10-30 **닫힘**): case 운영은 단순 매핑이 아니라 완전 매핑이다

등록할 때 적었던 진술은 **틀렸다**. "casefold·title·swapcase·istitle은 정확하므로 문제는 upper/lower
뿐"이라는 근거는 ASCII 표본 검사에서 나온 것이었다. 0x0000-0x10FFFF 전체에서 case 매핑이 항등이 아닌
코드 포인트 2,927개를 뽑아 9개 메서드를 전부 대비한 결과:

| 메서드 | 어긋난 코드 포인트 | 첫 사례 |
|---|---|---|
| `upper` | 623 | `"ß".upper()` → CPython `'SS'`, SharpPy `'ß'` |
| `lower` | 521 | `"İ".lower()` → CPython `'i\u0307'`, SharpPy `'İ'` |
| `swapcase` | 682 | `"ß".swapcase()` → CPython `'SS'` |
| `capitalize` | 627 | `"ß".capitalize()` → CPython `'Ss'` |
| `title` | 670 | `"ß".title()` → CPython `'Ss'` |
| `casefold` | 807 | `"µ".casefold()` → CPython `'μ'`(U+03BC), SharpPy 그대로 |
| `isupper` | 302 | `"Ⅰ".isupper()` → CPython `True` |
| `islower` | 303 | `"ͅ".islower()` → CPython `True` |
| `istitle` | 333 | `"ǅ".istitle()` → CPython `True` |

원인은 하나로 수렴한다. .NET의 `ToUpperInvariant`/`ToLowerInvariant`/`char.IsUpper`는 Unicode의
**단순(simple) 1:1 매핑**과 단순 속성만 구현하고, CPython은 **완전(full) 매핑**과
cased / case-ignorable / uppercase / lowercase / titlecase 속성을 쓴다. 단순 매핑이 항등인
Other_Uppercase / Other_Lowercase / Lt 문자(로마 숫자 Ⅰ, 동그라미 문자 Ⓐ, ǅ)에서는 속성 판정까지
함께 틀린다. `char.GetUnicodeCategory`로 만든 기존 `IsCasedChar`(Lu/Ll/Lt)도 같은 이유로 틀렸다.

### 구현: 표는 CPython에서 만들고, 알고리즘은 CPython 것을 옮긴다

- `core/UnicodeCaseData.cs` — **생성물**(`.artifacts/tmp/gen_unicode_case.py`).
  - 매핑: 기준 해석기 3.12.15에서 `chr(cp).upper()/.lower()/.title()/.casefold()`를 직접 측정해
    담았다. 다중 코드 포인트 확장 103개(U+00DF → `SS`, U+0130 → `i`+U+0307 …)가 이 방식으로 들어온다.
  - 속성: Python에는 코드 포인트 단위 API가 없으므로 고정된 `Objects/unicodetype_db.h`를
    `gettyperecord()`와 같은 경로(`index2[index1[ch>>7]<<7 | ch&0x7f]`)로 파싱해
    `LOWER 0x08 / TITLE 0x40 / UPPER 0x80 / CASE_IGNORABLE 0x1000 / CASED 0x2000` 비트를 읽었다.
    디코더는 기동 시 자체 점검(U+0041, U+01C5, U+2160, U+24B6, U+0300, U+0027, U+03A3, U+03C2)을 돈다.
    `'`가 case-ignorable로 나오는 것까지 CPython과 일치한다(`"Σ'Σ".lower() == "ς'ς"`의 근거).
- `core/PyUnicodeCase.cs` — `do_upper` / `do_lower` / `do_title` / `do_capitalize` / `do_swapcase` /
  `do_casefold` / `unicode_isupper_impl` / `unicode_islower_impl` / `unicode_istitle_impl`과
  `lower_ucs4` + `handle_capital_sigma`(그리스 대문자 시그마 하나뿐인 문맥 의존 매핑)의 필사.
  `capitalize`의 첫 문자가 **upper가 아니라 title** 매핑이라는 점, `title`의 `previous_is_cased`가
  **원본 문자열**을 본다는 점을 그대로 지킨다.
- `type/PyString.cs`에서 손으로 작성하던 `CaseFold`(다중 매핑 12개 하드코딩 + 나머지
  `ToLowerInvariant`)·`IsCasedChar`·`CPythonTitle`, 그리고 호출자 없던 `Upper(PyObject[])` /
  `Lower(PyObject[])` / `TitleMethod` / `Title()` / `IsUpper()` / `IsLower()` / `IsTitle()`을 삭제했다.
  case 운영만 해도 **구현이 네 겹**(descriptor / private helper / public instance method / static
  helper)이었다.

### 김에 정리한 것 (P0-29 계열, 측정이 근거)

- str 메서드 descriptor 46곳이 각자 쓰던 수신자 오류 문구를 `PyString.AsStrReceiver(self, name)`
  하나로 모았다. CPython `descr_check`의 문구는
  `descriptor 'upper' for 'str' objects doesn't apply to a 'int' object`이고, SharpPy가 쓰던
  `requires a 'str' object but received a 'int'`는 CPython이 만들지 않는 문구였다.
  **단, dunder 슬래퍼 래퍼는 사정이 다르다**: `str.__len__(5)`는 CPython도
  `descriptor '__len__' requires a 'str' object but received a 'int'`라고 답하므로 그 11곳은 그대로 뒀다.
- 과잉 인수를 거절하는 제로 인자 메서드 19곳의 문형에 정의 타입 접두를 붙였다:
  `str.upper() takes no arguments (1 given)` (측정: `str.isdigit()` 등 9개 모두 동일 계열).

### 회귀

- `tests/bughunt/p71_unicode_case.py`: 93케이스, CPython 대비 차이 0. 출력은 UTF-8 hex로 내보낸다
  — SharpPy는 문자열을 UTF-16 code unit으로 인덱싱해서(P0-34) `ascii()`/`len()`이 비교를 오염시킨다.
- 풀 매트릭스 26,343비교 + 문자열 코퍼스 4,140비교(시그마 문맥, 임의 혼합, 비-보조 평면 포함) 차이 0.
  성능 손질을 위해 만든 회귀들이 실제로 두 번(재사용 버퍼의 시그마 스캔, 블록 인덱스의 끝 범위
  검사 누락) 어긋남을 잡아냈다.
- `tools/run_bughunt.py` **76/76**, `tools/evaluate.py` 그대로 `225 / 3 / 2`, Godot 4.7.2 mono
  replay `82 matched, 0 other`.

## P0-32 (2026-10-30 **닫힘**): `float.fromhex`는 반올림까지 CPython의 것이다

`float.fromhex("0x1p3")`은 CPython `8.0`, SharpPy `0.0`이었다. 조용히 틀리는 것이 최악이다.

원인은 두 개였다. 가수 누적 루프 두 개가 점을 포함하지 않는 가수에서 서로를 무효화해서(첫 루프는
`j < fractionStart - 1`, 둘째 루프는 `j`가 이미 `coeffEnd`) 모든 자리에서 0.0이 나왔다. 그리고
파싱이 끝난 위치를 확인하지 않아 뒤의 잡음을 버렸다. 그런데 CPython 소스를 열어보니
(`Objects/floatobject.c:1311-1549`, 3.12에는 `float_fromhex_core`가 아니라 `float_fromhex`다)
단순 누적 방식 자체가 틀린 설계였다:

- CPython은 가수 비트를 뽑아 **round-half-even**으로 반올림한다.
  `0x1.00000000000008p+0` → `1.0`, `0x1.00000000000018p+0` → `1.0000000000000004`.
  누적 후 `ldexp` 방식은 이 두 개를 구별하지 못한다.
- 공백은 ASCII뿐이다 (`Py_ISSPACE`). NBSP는 `ValueError`.
- 부호 뒤·`p` 앞의 공백도 허용하지 않는다. 선후미 공백만.
- `inf`/`infinity`/`nan`은 부호를 포함해 자체 파싱 후 꼬리 공백 검사로 바로 넘어간다. `nan)abc`는 `ValueError`.
- 자릿수 상한을 넘으면 `ValueError: hexadecimal string too long to convert`,
  값이 커지면 `OverflowError: hexadecimal value too large to represent as a float`,
  그 외 파싱 실패는 `ValueError: invalid hexadecimal floating-point string` (접미사 없이).
- classmethod는 도달한 클래스로 값을 다시 만든다 (`PyObject_CallOneArg(type, result)`).
  따라서 `G.fromhex(...)`에서 `G.__new__`가 실행되고 예외를 던질 수도 있다.

C#에는 같은 알고리즘을 그대로 옮겼다 (`PyFloat.ParseHexFloat`). p69가 48개 명명 케이스 + 고정 16진
문자열 31개의 `fromhex(h).hex() == h` 왕복을 고정했다 (차이 0).


## P0-33 (2026-10-30 등록): `except*`와 ExceptionGroup이 PEP 654를 못 따라간다

이름 비교 매처를 MRO 매처 하나로 합치는 작업(p70)에서 드러났다. 매칭 규칙 문제는 정리됐고, 남은 것은
**그룹이 예외를 CLR 타입으로 저장하는 구조**다. 재현자는 `p70`에 넣지 않았다 - 프로브가 틀린 동작을
얼리는 걸로 끝나기 때문이다.

| 재현 | CPython 3.12.15 | SharpPy |
|---|---|---|
| `ExceptionGroup("g", [MyErr("a")])` (user class) | 그룹 생성됨 | `TypeError: second argument (exceptions) must be a non-empty sequence` |
| `ExceptionGroup("g", [MyErr("a"), 5])` | `ValueError: Item 1 of second argument (exceptions) is not an exception` | 위와 같은 TypeError |
| `try: raise ExceptionGroup("g",[ValueError("a"),TypeError("b")])` `except* ValueError` | `['ValueError']` 만 잡힘 | 미처리로 빠져나감 |
| 같은 코드에서 미처리 재던지기 | `ExceptionGroup` 재발생 | `RuntimeError: Unable to cast object of type 'SharpPy.PyNone'` |
| `ExceptionGroup("g",[MyErr("a")]).split(KeyError)` | `(None, group)` | 위 캐스트 크래시 경로 |

원인 (측정 근거):

1. `PyBaseExceptionGroup`가 `List<PyException>`을 들고 있다. 사용자 정의 예외는 `PyClassInstance`라
   리스트에 들어가지 못하고, 들어가지 못하면 "빈 시퀀스"가 되어 위 TypeError가 난다. CPython은
   `PyObject *excs`이며 각 항목이 `BaseException`의 인스턴스인지만 본다.
2. `PyVM.PrepReraiseStarExceptions`가 `exceptions.Add((PyException)item);`로 캐스트한다. 항목이
   `PyClassInstance`이거나 `PyNone`이면 `InvalidCastException`이다 (`core` 전반에 `List<PyException>`
   사용처 18곳).
3. `subgroup()`/`split()`의 조건 처리와 메시지, `ExceptionGroup`이 `BaseException` 전용 항목을 거부하는
   규칙(`ExceptionGroup`는 `Exception` 계열만 받는다)이 CPython과 대조되지 않았다.
4. intrinsic 인자 순서가 뒤바뀌어 있어서 `// But PrepReraiseStarExceptions expects (list, orig), so swap`
   라는 주석으로 보정되고 있다. CPython의 `PREP_RERAISE_STAR` 시그니처를 먼저 확인할 것.

고치는 순서: 그룹 payload를 `List<PyObject>`로 바꾸고 항목 검증을 MRO (`DerivesFromBaseException`)로
바꾼 뒤, `PrepReraiseStarExceptions`의 캐스트를 삭제하고, CPython `Objects/group.c`의 메시지
(`Item %d of second argument (exceptions) is not an exception`)를 옮긴다. 그 다음에 위의 재현자들을
프로브로 승격한다.

## P1 모음 (2026-10-30 검사): 같은 것을 두 번 구현한 자리

1. **(닫힘) builtin 표 항목과 모듈 delegate가 둘 다 있던 것.** 스캐너는 13개를 지목했지만 실제로
   중복이었던 것은 11개다 (`abs, ascii, bin, format, hex, isinstance, issubclass, len, memoryview,
   oct, vars`). `compile`과 `pow`는 `_sre.compile`, `operator.pow`라는 **같은 이름을 가진 다른 모듈
   속성**을 스캐너가 충돌로 읽은 오탐이었고, 등록은 원래부터 표 하나만 쓰고 있었다.
   `builtins` 등록이 delegate를 넘기기 때문에 `core/PyBuiltin.cs`의 표 항목은 실행되지 않는 코드였고,
   그중 `CallIsInstance`는 `Console.WriteLine("[isinstance] ...")` 디버그 출력을 무조건 내보내는
   상태였다. 표 등록 11개와 메서드 본문 361줄을 삭제했다. 삭제 전후로 60케이스 프로브 출력이
   의도한 3줄(`vars`/`format` 문구)을 제외하고 바이트 단위로 동일하다는 것을 확인했다.
2. **`operator.pow`/`operator.abs`가 `builtins`와 별도 구현** — CPython이 거부하는 keyword까지 받고,
   라벨에 `_operator.` 접두가 없다 (CPython `_operator.pow() takes no keyword arguments`). `abs`에서
   한 구현으로 모았을 때 따랐던 길을 다시 벗어났다.
3. **`set.copy`**가 `TypeDict`와 `GetAttribute` 특별 분기 두 곳에 등록되어 있다.
4. **(닫힘) 호출자가 없는 구 구현들** — `PyString.Upper()`, `PyString.Lower()`,
   `PyString.Capitalize()`를 삭제했다. 셋 다 소스 트리 전체에서 호출자가 0이었고
   `Capitalize`는 `CurrentCulture.TextInfo.ToTitleCase`를 썼다. 살아있는 `title`이 CPython 단어 경계
   규칙으로 의도적으로 옮겨간 것을 모르는 사람이 저 전화를 고치면 문화권 의존 회귀가 된다.
   `str.upper`/`str.lower`도 같은 이유로 문화권 헬퍼를 쓰지 않는다.
5. **(닫힘) 예외 이름 비교 경로.** 세 곳이 한 규칙을 나눠 가지고 있었다:
   `PyVM`의 `CHECK_EXC_MATCH` 분기 숲(CLR 이름 → CLR 타입 표로 `IsAssignableFrom`, user 예외는
   `OriginalClass`, `PyClassInstance`는 `InstanceType`, 이름 동등 폴백), `PyVM.ExceptionMatches`의
   `StringComparison.OrdinalIgnoreCase` + `"Exception"` 하드코딩(`except*` 경로),
   `PyBaseExceptionGroup.ExceptionMatches`(`"PyValueError"`과 `"ValueError"`을 비교).
   `runtime/PyVM.cs`의 `ExceptionMatchesHandler` 하나로 합쳤다: 튜플은 임의 요소, 예외 클래스가 아닌
   핸들러는 `catching classes that do not inherit from BaseException is not allowed`, 나머지는
   raise된 객체 자신의 타입 MRO를 참조 동등으로 훑는다. `type(ValueError('x')) is ValueError`,
   `type(MyErr('x')) is MyErr`, `ValueError in type(SubValue('x')).__mro__`가 모두 True라는 것을
   먼저 측정했고, 그래서 이름 폴백이 필요 없다. 같은 이름의 다른 클래스가 서로를 잡을 수 있었던
   (`m.Name == expected.Name`) 경로가 사라진 것을 p70의 `name_collision_not_caught`가 고정한다.
6. **codec 호출이 표 밖에서 4개 파일** — `modules/_CodecsModule.cs`(4), `runtime/PyBinaryTypes.cs`(3),
   `core/PyFileContextManager.cs`(1)가 `Encoding.GetEncoding`을 직접 불러서 `core/PyString.cs`의
   `NormalizeCodecName` 별칭 표와 다른 경로로 간다. 모두 그 함수로 모은다.
7. 인수 검사 문자열이 8계열 400개 넘게 흩어져 있다. 계량은 P0-29로 남긴다.

p70과 함께 닫힌 것: `vars()`가 `NotImplementedError`를 던지던 것을 `locals()`로 합쳤고,
`vars(1)`이 `AttributeError`를 새던 것을 `TypeError: vars() argument must have __dict__ attribute`로
고쳤고, `vars`/`format`의 개수 문구를 CPython의 손글씨 계열(`vars expected at most 1 argument, got 2`,
`format expected at least 1 argument, got 0`)로 바꿨다. `CHECK_EXC_MATCH` 축소는 예외 무거운
워크로드 A/B에서 TOTAL -1.4%였다 (플래그 없음).
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
| `sorted(vars(cls).keys())` (plain class) | `['__dict__', '__doc__', '__module__', '__weakref__']` | `__dict__`/`__weakref__` 없고 `__qualname__` 추가 |
| `iter(vars(int))` | dict_keyiterator | **TypeError: 'mappingproxy' object is not iterable** |
| `len(vars(int))` | 63 | 58 |
| `locals() is locals()` (모듈 최상위) | True | **False** - `locals()`가 매번 사본을 만든다 |

세 줄은 오늘 `vars()`를 `locals()`로 합치면서 새로 측정한 것이다. 마지막 줄은 특히 CPython에서
최상위 `locals()`가 모듈 `__dict__` 그 자체라는 사실(`vars() is locals()`가 True)을 놓치고 있었다.

동작 자체는 같은 경로로 된다(`frozenset([1]).union({2})`, `complex(1,2).conjugate()`,
`memoryview(b"ab").cast("B")` 모두 정확). 디스패치는 맞고 검사 API만 틀린 상태다. `dir()` 내용
부족(기존 P1)과 같은 단계에서 합쳐 처리할 가능성이 있다.

### 가드

`tests/bughunt/p65_registration_order.py` — 객체를 만들기 전에 타입 표면을 읽는 순서를 고정한다.
지금 맞는 부분까지 검증하고, 위 남은 6건은 이 항목에 문자로 남기되 프로브가 같이 무너지지는
않도록 넣지 않았다.


## P0-34 (2026-10-30 등록): `str`은 UTF-16 code unit으로 인덱싱한다 (비-보조 평면 전체)

P0-31을 비-ASCII로 확장하다가 드러났다. case 운영은 이제 코드 포인트 단위로 움직이지만 SharpPy의
`str` 자체는 여전히 .NET `string`의 UTF-16 unit을 쓴다.

| 표현 | CPython | SharpPy |
|---|---|---|
| `len("\U0001F600")` | `1` | `2` |
| `ord("\U0001F600"[0])` | `0x1F600` | `0xD800` |
| `"x\U0001F600"[:2]` | `'x\U0001F600'` | 서rogate 절반에서 잘림 |
| `"%c" % 0x1F600` | `'\U0001F600'` | `ValueError: %c arg not in range(0x10000)` |
| `ascii("\U0001F600")` | `'\U0001f600'` | `'\ud800\ude00'` |

`UnicodeCaseData.AppendCp`가 서서히 surrogate pair를 조합하도록 만든 것은 이 격차를 case 운영
내부에서라도 없애기 위한 것이었다. 문자열 자체는 `char` 인덱스·`Length`·`Substring`에 기대고 있어서,
CPython처럼 코드 포인트 인덱스로 바꾸려면 `PyString`의 표현 자체(UTF-32 또는 latin-1/ucs2/ucs4
3단)를 옮겨야 한다. CPython이 `PyUnicodeObject`의 kind을 세 가지로 나눈 이유와 같다. 파급 면이
넓어서(슬라이스, `find`/`index`, `encode`, `sys.stdout` 인코딩, 포맷, 정규식 매칭 위치) 별도 항목으로
두고 착수 순서를 기다린다. 당장의 우회는 없다 — 비-보조 평면을 쓰는 프로그램은 표시·인덱스가 모두
틀리다.

### 2026-10-30 조사 완료 (`docs/reports/2026-10-30/p034_string_representation_survey.md`)

49케이스 실측에서 **28개 어긋남**을 확정했다. 길이·인덱싱·슬라이스·반복·검색 오프셋·패딩·정밀도·
`chr`/`ord`/`%c`·`isalpha`·`sorted`/`max`가 전부 UTF-16 unit 기준이다. 여기에 두 가지가 더 있다:

- **순서 자체가 다르다**: `sorted([chr(0x10FFFF), chr(0xE000), chr(0xFFFF), chr(0x41)])`가 CPython
  `['A','','￿','􏿿']`, SharpPy `['A','􏿿','','￿']`.
  비교는 `StringComparison.Ordinal`(culture 아님)이지만 UTF-16 사전순은 서로게이트 범위 때문에
  U+E000-U+FFFF와 astral 사이에서 **코드 포인트 순서와 어긋난다**. `min/max/sorted`에 직접 영향.
- 정상 Python `TypeError` 뒤에도 `--- C# Exception Details ---` 블록이 따라 나온다.

진입점(`Length`/`GetItem`/`GetSlice`/비교 4곳)은 존재하지만 `Value.Length` 115지점 중 **69지점이
`InitializeStringDescriptors` 인라인**이다. 권장안은 UTF-16 저장을 유지하고 **코드 포인트 시맨틱을
`core/UnicodeText.cs` 한 곳**으로 모은 뒤 인라인 산술을 흡수 삭제하는 3커밋 계획이다 (A: PEP 393
3종 저장은 `Value` 소비자 전수 정리가 선행돼야 해서 현 단계 각하, C: `int[]` 1급 저장은 숨은 O(n)).
프로브 원자료: `.artifacts/tmp/p34_survey.py`.

### 2026-10-30 사전 작업 1단계 착수 (`docs/reports/2026-10-30/p034_value_consumer_inventory.md`)

`PyString.Value` 소비자 장부를 만들었다(외부 239지점 + PyString.cs 내부 bare `Value` 314언급).
첫 라우팅으로 **중복 구현이 줄었다**: `len(str)`을 계산하던 4곳(VM `len` 빠른길, starred unpack,
`len()` 상수 폴딩, `n * str` 폭발 가드, `PyIOBase`)을 `PyString.Length()` 하나로, 텍스트 비교 8곳
(`PyString` 4 + `PyStrSubclass` 4)을 `PyString.CompareText` 하나로, `ord()`는 `CodePointAt`으로 모았다.
거동은 전달만 했으므로 그대로이고, flip은 `CompareText`/`CodePointAt` 한 곳에서 일어나게 뒀다.
남은 코호트: codec 경계(`_CodecsModule` 소비 위치 반환 7곳) → PyString.cs 내부 115지점 흡수 삭제 →
parse/compile 열 좌표 → regex/`StringIO` 좌표 → 진짜 interop 6지점의 명시적 탈출구화.

## P0-35 (2026-10-30 등록): 소스 디코딩 — NUL 바이트와 decode 실패

| 표현 | CPython | SharpPy |
|---|---|---|
| 소스에 NUL 바이트 | `SyntaxError: source code cannot contain null bytes` | 조용히 넘어간다 |
| 파일에 무효 UTF-8 바이트 | `SyntaxError: Non-UTF-8 code starting with 'ÿ' in file <path> on line N` | CLR 스택 트레이스가 그대로 새어나온다 (`Generated.PyParserRuntime.ParseSource` ...) |

측정 그대로 적는다. SharpPy는 파일을 읽을 때 `U+FFFD`로 대체 디코딩하고, 그 문자가 토크나이저를
이상한 상태로 만들어 CPython에는 존재하지 않는 CLR 예외가 난다. 즉 **입력 단계의 거절**이 없다:
CPython은 바이트→문자 디코딩(`Py_DecodeUTF8` strict + NUL 검사)에서 멈추고, SharpPy는 오염된 문자로
계속 진행한 뒤 엉뚱한 곳에서 무너진다. 진입점 한 곳을 strict 디코딩 + NUL 검사로 바꾸면 두 증상이
사라진다.

## P0-36 (2026-10-30 등록): case 운영은 CLR intrinsic보다 비싸다 (측정된 대가)

P0-31을 닫으면서 정확성을 사서 온 비용이다. 합성 하니스(200-540자 문자열 25,000회, 3-5라운드 best)로
같은 커밋 worktree와 교차 측정한 값:

| workload | 이전 | 이후 | 증감 |
|---|---|---|---|
| `upper()` ASCII (변화 있음) | 0.0653 | 0.0906 | +39% |
| `lower()` ASCII (변화 없음) | 0.0651 | 0.0874 | +34% |
| `upper()` 그리스어 | 0.0388 | 0.0856 | +121% |
| `lower()` 그리스어 | 0.0392 | 0.1367 | +249% |
| `casefold()` 그리스어 | 0.0716 | 0.1523 | +113% |
| `capitalize()` 혼합 | 0.0356 | 0.0779 | +119% |
| `swapcase()` 혼합 | 0.0591 | 0.0777 | +31% |
| `isupper()` ASCII | 0.0199 | 0.0201 | +1% |
| `istitle()` 혼합 | 0.0204 | 0.0217 | +6% |

ASCII 경로는 "전부 ASCII인가"를 한 번 스캔한 뒤 `ToUpperInvariant`/`ToLowerInvariant`에 위임한다 —
ASCII 범위에서 그 intrinsic이 CPython의 ASCII 매핑과 동일하고(문화 개입 여지 없음) 벡터화돼 있기
때문이다. 남는 +35%는 그 스캔 한 바퀴다. 비-ASCII는 블록 인덱스(256 코드 포인트 단위)로 표를 찾아도
`int[]` 채우기 + `StringBuilder` 경로가 남는다. 후보: 목적지를 `char[]`/`string.Create`로 두고
UTF-16을 직접 걸어 중간 `int[]` 패스를 없애기, 시그마가 없는 문자열은 배열 경로 없이 바로 쓰기.
`tools/perf_probe.py`의 표준 스위트에는 영향이 없다(case 운영을 부르지 않는다): old/new 교차에서
`str_fmt +3.6%`, `list_comp -0.4%`, 나머지 ±0. 몇 라운드 눈에 띈 `list_comp +71%`는 같은 바이너리
재측정에서 -0.8%로 돌아오는 기계 상태 노이즈였다.

## P1 (2026-10-30 등록): 평가된 소스의 SyntaxError에 위치 정보가 없다

`eval("%s")` (그 자체로 잘못된 코드)을 CPython이 `SyntaxError: invalid syntax (<string>, line 1)`로
내는 반면, SharpPy는 `SyntaxError: invalid syntax`만 낸다. `compile()`/`eval()` 경로에서 파일명·줄
번호가 붙지 않는다. traceback 표시와 같은 계열(`p17`)이지만 진입점이 다르다.

## P0-34 pre-work 코호트 1 (2026-10-30 완료): 코덱 경계

`str.encode`, `bytes(str, enc)`, `str(bytes, enc, errors)`, `bytes.decode`, `_codecs.*_encode`
다섯 곳이 저마다의 코덱 표를 갖고 있었다. .NET 인코더는 인코딩할 수 없는 것을 `?`(또는 U+FFFD)로
바꾸므로 라이브 쪽 경로는 `'é'.encode('ascii')`에 `b'?'`, lone surrogate에 `b'\xef\xbf\xbd'`를
답했다. CPython은 거부한다. `PyString.Encode`에는 이미 faithful한 거부 코드가 있었는데 아무도
부르지 않는 사본이었다. 이제 인코딩은 `PyString.Encode`, 디코딩은
`PyStringNewArguments.DecodeBytes` 한 곳으로 모았다. 데드 사본 `PyString.FromBytes`는 삭제.
증거: `tests/bughunt/p72_encode_strict.py` 106 case diff 0.

이 코호트에서 함께 고친 것:
- `utf-16`/`utf-32`는 BOM 코덱인데 별칭이 `-le`로 접혀 있어 BOM이 사라졌다 (`"ab".encode("utf-16")`
  가 `b'a\x00b\x00'`). `_codecs.utf_32_le_encode`는 `utf_32_encode`에 위임해 BOM 없는 코덱에 BOM을 붙였다.
- 디코딩이 BOM을 결과에 남겼다 (`'\ufeff...'`). BOM을 소비하고 BE 마크는 바이트 순서를 뒤집는다.
- `utf-32-be`가 little-endian으로 디코딩돼 라운드트립이 U+FFFD로 망가졌다.
- 거부 메시지의 escape 폭(`\xNN`/`\uXXXX`/`\UXXXXXXXX`)과 run 셀 수. 한 문자 문자열에서
  "characters in position 0-1"이라고 답했다.
- `_codecs` 인코딩 진입점의 이름·arity 메시지 (`utf_8_encode expected at most 2 arguments, got 3`,
  `utf_8_encode() argument 1 must be str, not bytes`).

P0-34에 묶여 남는 것 (인코더의 버그가 아니라 저장 표현의 한계):
- `_codecs.utf_8_encode(chr(0x10400))`의 소비 수가 2 (CPython은 1). `PyString.Length()`가 아직
  code unit 기준이기 때문이고, 자리는 `Encoded()` 한 곳이다.
- CPython의 "별도 surrogate 코드포인트 두 개"는 .NET `string`으로 표현할 수 없다.
  `'\ud800\udc00'.encode('utf-16')`에서 SharpPy는 유효한 서러게이트 쌍으로 봐 CESU 바이트를 내고
  CPython은 거부한다.
- repr/len이 astral 문자를 서러게이트 쌍으로 보여주는 것(기존 P0-34 본체).

별도 잔여:
- 코덱 표는 utf-8/utf-16*/utf-32*/ascii/latin-1까지. 그 밖의 코드 페이지는 여전히 LookupError
  (아래 P1 codec 항목에 이미 등록).
- `surrogateescape`는 utf-8의 U+DC80..U+DCFF만 구현했다. 다른 코덱에서의 동작은 측정하지 않았고
  지금은 strict 메시지로 거부한다.
- `bytes.decode`의 utf-8/ascii 경로는 자체 구현(`DecodeUtf8Python`/`DecodeAsciiPython`)을 유지한다.
  바이트 위치 메시지가 `DecodeBytes`보다 정확하기 때문이고, 그 밖의 코덱은 `DecodeBytes`로 위임한다.

- `namereplace` 핸들러는 미 구현: `chr(0xE9).encode('ascii', 'namereplace')`가 CPython은
  `b'\N{LATIN SMALL LETTER E WITH ACUTE}'`, SharpPy는 `LookupError: unknown error handler name
  'namereplace'`. Unicode 이름 표(= `unicodedata.name`의 근거 자료)가 있어야 한다.
  `backslashreplace`와 `xmlcharrefreplace`는 p72로 고정됨.

### P1 codec registry follow-ups (측정으로 고정, 2026-10-30)

p73이 통과하는 범위 밖으로, 다음 다섯 family이 열려 있다. 전부 CPython 3.12.15에서 직접
측정해 두른 것이다 (`.artifacts/tmp/reg_spec*.py`, 출력 `.artifacts/tmp/reg_spec*_c.txt`).

1. **`CodecInfo`의 repr과 tuple 서브클래스 지위.** CPython은
   `repr(codecs.lookup('utf-8'))`가
   `<codecs.CodecInfo object for encoding utf-8 at 0x...>`이고 `ci[1]`은
   `<function decode at 0x...>`이며 `ci[2]/ci[3]`은 `encodings.utf_8.StreamReader/StreamWriter`
   클래스다. SharpPy는 `codecs.CodecInfo(...)`를 Python에서 호출해 객체를 만들지만
   `tuple` 서브클래스의 `__new__`/`__repr__`가 honoring되지 않아 repr이 평범한 tuple repr이고,
   `ci[2]/ci[3]`은 `codecs.StreamReader/StreamWriter`다 (`unpack a,b,c,d = ci`의 bool 판정은
   이제 일치). 원인은 코덱이 아니라 interpreter의 `__new__` 처리 — P0 후보 후보.
2. **코덱별 `StreamReader`/`StreamWriter`/`Incremental{Encoder,Decoder}`가 없다.** CPython은
   `encodings/utf_8.py` 등이 자체 클래스를 정의하고, SharpPy는 `codecs`의 base 클래스를 넣는다.
   클래스 *생성*은 되고 이름도 같지만 module이 다르고, base `IncrementalEncoder.encode`는
   `NotImplementedError`라 실제 증분 인코딩은 동작하지 않는다.
3. **구현되지 않은 코덱 family.** 별칭 표는 CPython `Lib/encodings/aliases.py`에서 생성했지만
   엔트리 함수가 있는 코덱은 utf-8/utf-8-sig/utf-16*/utf-32*/ascii/iso8859-1 + rot-13/hex/base64
   뿐이다. CPython이 응답하는 `cp1252`, `cp1251`, `big5`, `shift_jis`, `koi8-r`, `iso8859-*`,
   `mac_roman`, `unicode_escape`, `raw_unicode_escape`, `utf_7`, `punycode`, `idna`,
   `quopri`, `uu`, 압축 코덱(`zlib`/`bz2`)은 여전히 `unknown encoding`. charmap 계열은
   `PyStringNewArguments.ResolveCodec`이 코드 페이지를 아는데도 엔트리가 없어 노출되지 않는다.
4. **`binascii.Error`의 타입 이름.** 메시지는 같다 (`Odd-length string`,
   `Non-hexadecimal digit found`, `Incorrect padding`) 하지만 SharpPy는 `ValueError`로 표시된다.
   CPython은 `binascii.Error`(ValueError 서브클래스)고 `except binascii.Error`가 성립한다.
   SharpPy에는 전용 PyType이 없어서 `binascii.Error` 속성 자체가 없다.
5. **`_codecs` 함수 표면 36 vs 45.** 빠져 있는 이름: `code_page_decode/encode`,
   `mbcs_decode/encode`, `oem_decode/encode`, `readbuffer_encode`, `utf_7_decode/encode`
   (그리고 `unregister`는 추가됨). `dir(codecs)`도 81 vs 90. 3번과 같은 원인이다.

### P0-33 remainder (측정으로 고정, 2026-10-30)

p74가 통과하는 범위 밖. 각각 CPython 3.12.15에서 직접 측정해 두었다.

1. **처리되지 않은 그룹의 traceback 배치.** CPython은 `+ Exception Group Traceback (most recent
   call last):` 헤더와 `+-+---------------- 1 ----------------` 서브 블록 안에 멤버를 인쇄한다.
   이 interpreter는 일반 traceback 한 장 뒤 `ExceptionGroup: g (1 sub-exception)`만 인쇄
   (`.artifacts/tmp/x1.py`). `Lib/traceback.py`의 그룹 포맷터에 대응하는 native 출력이 없다.
2. **traceback이 stdout으로 나간다.** CPython은 stderr. `tools/evaluate.py`가 stdout을 비교하므로
   현재 게이트는 이 차이를 보지 못한다. 스트림을 옮기면 미caught 예외 fixture들이 한꺼번에
   바뀌므로 별도 단계로 처리해야 한다.
3. **`__notes__`/`add_note` 미구현.** 저장소가 없어서 CPython의
   `is_same_exception_metadata`가 비교하는 네 필드 중 notes가 관찰 불가능하고,
   `exceptiongroup_subset`의 `__notes__` 복사도 따라갈 것이 없다.
4. **`KeyboardInterrupt` · `SystemExit` · `EncodingWarning`이 builtins에 없다** (`NameError`).
   그래서 `BaseExceptionGroup`의 "BaseException 전용 멤버" 규칙을 Python에서 만들 수 없고,
   `ExceptionGroup('m',[KeyboardInterrupt()])` → `TypeError: Cannot nest BaseExceptions in an
   ExceptionGroup` 경로도 고정할 수 없다.별그 관찰: `dir(builtins)`는 이 이름들을 CPython과 같은
   62개 목록으로 보고하지만 `hasattr(builtins, name)`과 이름 조회는 실패한다 — dir과 속성 조회가
   서로 다른 표를 본다.
5. **`class Custom(ExceptionGroup)` 의 타입 보존.** SharpPy는 `Custom('c', [...])`에서
   `ExceptionGroup` 인스턴스를 돌려준다. ceval은 `exc.split(...)`을 속성 호출로 부르므로, 사용자
   재정의가 호출되지 않는다. builtin 서브클래스 동일성(P0-27계열)과 같은 뿌리.
6. **`except*` 절 안 `return`/`break`/`continue`.** CPython은
   `SyntaxError: 'break', 'continue' and 'return' cannot appear in an except* block`,
   SharpPy는 컴파일 through. `Python/compile.c`의 except* 게이트에 대응하는 검증이 없다.

함께 닫은 것: 던져진 모든 예외에서 stdout에 두 번 째로 인쇄되던 `--- C# Exception Details ---`
+ CLR 스택 덤프(`core/FileExecutor.cs`의 `TEMPORARY` 디버그 출력) 제거. 종료 코드는 CPython과
같이 1. 인터프리터가 보고하지 않은 failure만 stderr로 간다.

### P0-34 remainder (측정으로 고정, 2026-10-30)

코호트 2가 좌표계를 하나로 합친 뒤에 남는 것. 각각 CPython 3.12.15에서 직접 측정해 두었다.

1. **인접한 고아 서러게이트는 표현 자체가구분하지 못한다.** CPython은
   `chr(0xD800) + chr(0xDC00)`를 code point 두 개로 보관한다 (`len` 2,
   `find(chr(0xDC00))` → 1). SharpPy의 `.NET string`은 같은 내용을 U+10000 **하나**로 읽는다
   (`len` 1, `find` -1). PEP 393식 저장을 채택하지 않은 대가로 남는 본질적 한계 —
   코호트 1~5는 좌표계만 정리했고 이 모호성은 남긴다.
2. **`'\U0000d800'` 이스케이프가 파서를 죽인다.** CPython은 이를 허용한다 (`len(s) == 1`,
   `ord(s) == 0xd800`). SharpPy는
   `RuntimeError: A valid UTF32 value is between 0x000000 and 0x10ffff, inclusive, and should not
   include surrogate codepoint values` (`PyParserRuntime.ProcessEscapeSequences`가
   `System.Char.ConvertFromUtf32`에 서러게이트를 넘겨 CLR 예외가 그대로 드러난다). Python 레벨
   `SyntaxError`가 아니라 CLR 진단이라는 점이 별도 결함. p75는 그래서 고아 서러게이트를 `chr()`로만 만든다.
3. **`isdigit`는 Nd만 본다.** CPython은 Additional_Numeric_Property(digit)도 참으로 본다:
   `'²'.isdigit()` → True, `'①'.isdigit()` → True. SharpPy는 False.
   (`isdecimal`은 Nd로 일치: `'٣'`, `'𝟙'` 모두 이제 True.)
4. **`isprintable`은 Cn(미배정)을 거르지 못한다.** .NET에 미배정 코드 포인트를 나타내는 카테고리가
   없어서 Cc/Cf/Cs/Co/Zl/Zp/Zs만 본다. CPython 표(`Objects/unicodetype_db.h`)가 필요하다.
5. **`isidentifier`는 근사다.** XID_Start/XID_Continue 표와 NFC 정규화 요구 대신 카테고리를 쓴다.
   astral 첫 문자(`'\U00010000x'`)는 이제 맞지만 NFC 요구는 미충족.
6. **`Value.Length` 잔존 107지점** (코호트 3~5): `type/PyBytes.cs` 36, `modules/_SreModule.cs` 6,
   `core/PyCodecRegistry.cs` 3, `core/PyFileContextManager.cs` 3, `type/PyString.cs` 26(인코딩 루프·
   repr·내부 검색처럼 unit이 맞는 단위 포함).
7. **`bmp_methods` +7%** (교대 4라운드 실측, 기준 `bff3f11`): 창 정규화와 쌍 정렬 검사가 늘어난 비용.
   astral 워크로드와 `%` 포맷은 변화 없음(1.00x), `len_loop`/`list_comp`는 오히려 0.95~0.96x.

## P0-37 remainder (측정으로 고정, 2026-10-30)

수명은 CLR GC가 소유하고 참조 카운팅은 도입하지 않는다는 결정(P0-37)에서 파생된 잔여.
`tools/run_bughunt.py` p76은 **합의된 범위만** 고정하고, 아래는 고정하지 않는다(고정하면
알려진 격차를 통과 사례로 세탁하므로).

1. **조기 도달 객체의 `__del__` 타이밍** — CPython은 마지막 참조가 사라진 즉시 부른다.
   측정(`.artifacts/tmp/del1.py`): 함수 지역 객체를 버린 직후 CPython은 `del ran`을 출력하고
   SharpPy는 출력하지 않는다(종료 시점에야 실행). 종료 전 순서가 관측되는 프로그램은 결과가
   달라질 수 있다. 재논의 트리거: 실제 라이브러리 코드가 이 타이밍에 의존할 때.
2. **`gc.get_objects()` / `gc.get_referrers()` 내용** — CPython은 추적 객체 목록을 순회해
   `get_objects()`가 수천 개를 반환하고 `get_referrers`는 역참조를 찾는다. SharpPy는 객체를
   추적하지 않으므로 빈 목록(`.artifacts/tmp/gc2.py` 18행 실측).
3. **`gc.get_count()` / `get_stats()` 값** — 생성 카운터가 없어 `(0, 0, 0)` / 0. CPython 값은
   프로세스마다 달라 어차피 고정 불가능하지만, SharpPy는 "모은 적이 없다"를 일관되게 답한다.
4. **`gc.collect()`가 순환 미수거를 보고하지 않음** — 도달 불가능 주기를 찾을 수 없으므로 0을
   반환하고 사이클을 실제로 해제하지 않는다(CLR이 대신 수거한다). `gc.callbacks`는 실제로
   호출된다(p76 고정).
5. **finalizer 등록 목록이 종료까지 줄지 않음** — `__del__`을 선언한 클래스의 인스턴스를
   많이 만드는 프로그램은 `WeakReference` 항목이 쌓인다. 실측 100,000 생성 +2.6%
   (`.artifacts/tmp/w_watch.py`, 기준 `fb625c4`와 교대 3라운드). 일반 클래스 생성은 1.00x
   (`PyClass.DeclaresFinalizer()` 필드 한 번 읽기).
6. **재부활(resurrection) 미구현** — CPython은 `__del__` 안에서 자기 자신을 살려 두 번째
   호출을 허용한다. SharpPy는 한 번 확정하면 다시 부르지 않는다.
7. **`sys.getrefcount`가 실제 값을 말하지 않음** — 측정: CPython은 지역+인자 참조로 `>= 2`,
   SharpPy는 상수 `1`. 카운팅 메커니즘이 없어 정직한 값이 존재하지 않으므로, 값이 관측되는
   이 함수는 이 항목에 남긴다(에러는 발생하지 않는다).
8. **`weakref` 미구현** — `ModuleNotFoundError`. CPython은 내장 `_weakref` + `Lib/weakref.py` +
   `Lib/_weakrefset.py` 구조이며, SharpPy는 `itertools`/`_collections_abc`를 이미 가지고 있어
   같은 구조로 갈 수 있다. 네이티브 `_weakref`(ref/proxy/getweakrefcount/getweakrefs/
   `_remove_dead_weakref`)가 남아 있는 작업. 사망 통지는 CLR 수거 시점이 아니라 참조 접근
   시점에 결정되므로 콜백 타이밍이 추가 격차가 된다.
9. ~~**`PyFinalizer`의 "1회만" 판정이 참조 동일성이 아님**~~ → **교정함** (`ReferenceEqualityComparer`).
   다만 이것이 관찰 가능한 버그였는지는 측정으로 기각되었다. 기준 커밋 `83b23b1` worktree 빌드와
   수정 후 빌드에 같은 프로브를 돌려 출력이 완전히 같았다: (a) `__hash__`가 예외를 던지는 클래스,
   (b) `__eq__`가 항상 True이고 `__hash__`가 0인 클래스의 두 인스턴스, (c) `__hash__ = None`,
   (d) 값이 같은 `str`/`int`/`list` 서브클래스 인스턴스 두 개. 이유는 `PyObject.ToHash()` 의 기저
   구현이 `RuntimeHelpers.GetHashCode(this)`(참조 identity)이고 Python `__hash__` dispatch는 별도
   함수인 `GetPythonHash()` 가 담당하기 때문이다(`core/PyObject.cs:125-146`). 따라서 이 변경은
   **우연한 정확성을 명시적 정확성으로 바꾼 hardening** 이고, 이제 CPython의 `_PyGC_FINALIZED`
   플래그(`Objects/object.c:362`) 와 같은 의미를 갖는다. (a)(b)(c)는 p76에 고정.
10. ~~**C방(자원 상태) 실패**~~ → **P0-38로 해결** (아래 별도 절). 잔여는 타이밍 항목만 남는다.

이 항목들의 판단 근거·기각된 선택지·CPython 앵커 표는 **`docs/lifetime_and_gc_policy.md`** 에 있다.

## P0-38 resource release bus (2026-10-30 닫힘)

실측된 실패가 닫혔다. `.artifacts/tmp/lock.py`(4000회 `open(p,'a')` 후 `os.remove(p)`):
CPython `remove ok` / SharpPy `remove failed: PermissionError` → **이제 양쪽 `remove ok`**.

- `core/PyResourceBus.cs` (new): 파일 객체의 weak 참조 + **객체가 아니라 stream을 캡처한**
  release action 등록부. `this`를 캡처하면 등록부가 객체를 살려 두므로 해제가 영구 불가 —
  그 점이 이 설계의 핵심 제약이다.
- 발화점 4개: `close()`/`with`(결정적, CPython과 동일) · 등록 주도 sweep(기하급 임계치라
  20000회 open도 2차수가 되지 않음) · `gc.collect()`(reconciliation 앵커) · 종료 시
  (finalizer 이후 — `__del__`이 열려 있는 파일에 쓸 수 있으므로).
- `os.remove`/`unlink`/`rmdir`/`rename`은 실패 시 해제를 시도하고 **한 번만** 재시도한다
  (`modules/NtModule.cs RetryAfterRelease`). 성공 경로 비용은 0이고, 이미 실패하던 경로에만
  든다. CPython은 애초에 그 상태가 되지 않는다(참조 수가 0이 되어 `iobase_finalize`가
  `close()`를 부름, `Modules/_io/iobase.c:298`).
- 프로브 `tests/bughunt/p77_resource_release.py`(116줄, diff 0)가 고정하는 것:
  재바인딩 누수 루프 제거 성공, 리스트 버림+`collect()` 후 제거 성공, 버려진 쓰기 내용의
  가독성, `with` 후 `.closed is True`, **살아 있는 파일은 컬렉트 후에도 해제되지 않음**,
  버려진 writer 후 `rename`, 마지막 핸들이 살아 있으면 제거 실패(양쪽 동일), 1500개 누수 후 제거.
- 성능(`.artifacts/tmp/lockcase/p38_ab.py`, 기준 `132c65d` worktree와 5라운드 교대):
  `closed`(with 4000) **0.97x**, `compute` **1.00x**, `abandoned`(닫지 않은 3000)
  **1.37x 느림** — 그러나 CPython 대비 **0.93x**. 기준 코드가 빨랐던 이유는 handle 3000개를
  finalizer 스레드에 미우면서 백그라운드에서 해체했기 때문이다. 같은 정직한 작업을 CPython보다
  덜 쓰는 셈이므로 이 비용은 수용한다.
- Godot 4.7.2 mono 리플레이 82 matched, 0 other.

### P0-38 remainder

1. **해제 시점은 여전히 관측점이지 사망 시점이 아니다** — CPython은 마지막 참조가 사라진
   즉시 닫는다. SharpPy는 `open()` 시 sweep, `gc.collect()`, 제거/이름변경 실패 시,
   그리고 종료 시에 닫는다. 결과가 관측에 영향을 주지 않도록 제거·이름변경 재시도로
   방어했지만, "잠금 여부"를 `os` 밖에서 관찰하는 경로(예: 다른 프로세스와의 경쟁)는
   여전히 타이밍 차이가 난다.
2. **`close()` 오버라이드는 사망 시 불리지 않는다** — CPython의 `iobase_finalize`는 객체에서
   Python 수준 `close()`를 찾아 호출하므로 서브클래스 재정의가 실행된다. SharpPy의 bus는
   네이티브 해제만 수행한다. 이 시점에 객체는 이미 수집되어 dispatch 대상이 없다.
3. **`ResourceWarning`을 내지 않는다** — CPython은 미해제 파일이 수집될 때 경고한다
   (`Modules/_io/fileio.c:90`). SharpPy는 대신 닫아주므로 경고가 없다(기본 필터가 무시하지만
   `-W error` 실행에서는 차이가 난다).
4. **`open()` 경로 자체는 재시도하지 않는다** — handle 고갈성 실패는 미측정이며, Windows
   handle 상한을 실제로 소진하는 프로브는 만들지 않았다.
5. **자원 타입 확대 미착수** — socket·Godot 리소스 등은 아직 bus에 등록되지 않았다.
   `PyFile`이 첫 고객이고, 등록부는 임의의 `Action`을 받도록 이미 일반화되어 있다.

## P0-40 용기 클래스 서브클래스 → 닫힘 (2026-10-30)

`.artifacts/tmp/submatrix.py`가 9개 내장 타입의 서브클래스를 같은 표면(repr/str, len, 인덱스,
슬라이스, 순회, eq, `__mro__`, 메서드 위임)으로 비교한 결과 **4/9 갈린다**:

| 기본형 | 판정 | 갈리는 것 |
|---|---|---|
| `list` | DIFF | `repr(x)`/`str(x)` 가 `<__main__.C object at 0x…>` — CPython은 `[1, 2, 3]`. len/idx/slice/iter/eq/mro 은 정상이므로 **repr 경로만** 빠짐 |
| `dict` | DIFF | 동일 — `{'a': 1}` 대신 object repr. 나머지는 정상 |
| `set` | DIFF | **생성자가 인자를 거부**: `C([1, 2])` → `TypeError: C() takes no arguments` (스크립트 중단) |
| `frozenset` | DIFF | 동일 — `C([1])` 거부 |
| `tuple` `bytes` `str` `int` `float` | OK | — |

원인은 이름으로 드러난다. 서브클래스 래퍼가 `PyTupleSubclass`(`type/PyClass.cs:2523`),
`PyStrSubclass`, `PyBytesSubclass`, `PyIntSubclass`, `PyFloatSubclass` **다섯 개만** 있고
list/dict/set/frozenset 대응이 없다. 생성 시 래퍼를 고르는 지점에서 빠지면 object 기본 repr로
떨어지고, set 계열은 생성자 인자 파싱 자체가 연결되지 않는다.
지난 작업 [tuple 서브클래스와 사라진 CustomType](reports/2026-10-30/tuple_subclass_and_the_missing_customtype.md),
[컨테이너 서브클래스 dunder](reports/2026-10-30/container_subclass_dunders.md) 범위에서 repr과
set/frozenset 생성자는 빠졌다.

**해결**._repr만 빠진 것_이 아니었다. 측정을 넓히면 set/frozenset 서브클래스는 전혀 작동하지
않았고(`len` 실패、`add` 실패、생성자 거부)、비교·truthiness·hash·연산자 전체가 서브클래스에서
사라져 있었다. 기준은 CPython의 **슬롯 상속**(`inherit_slots`/`update_one_slot`) 하나로 잡았다 —
서브클래스에 메서드를 복사하는 게 아니라 기본형의 C 슬롯을 그대로 갖는 것.

| 기관 | 앵커 |
|---|---|
| set_repr가 `tp_name`을 출력(`C({1,2})`, `C()`) | `Objects/setobject.c:507` |
| `set_new`는 iterable을 무시, `set_init`가 소비(문구의 이름 두 개가 다름) | `setobject.c:1038`, `:1977` |
| frozenset는 tp_init 자체가 없음 → 값은 tp_new에서만 만들어진다 | `setobject.c:1003`, `:2262` |
| 키워드 거부가 서브클래스에도 적용(`F(x=1)` Raises) | `frozenset_new`의 `tp_init` 비교 |
| eq/concat/merge는 서브클래스를 허용 | `PyList_Check`/`PyDict_Check`/`PyAnySet_Check` |
| `bool(C())` False | `Objects/object.c:1675` — nb_bool → sq_length/mp_length |
| `x += y`는 객체를 돌려준다 | `nb_inplace_add`/`set_inplace_union` |
| 재귀 repr은 `[...]` | `Py_ReprEnter` |

구현: `core/PyNativeStorage.cs`(신규 — 값 접근의 단일 지점, `PyList`의 **리플렉션** 2곳과
`PyFrozenSet` 정적 생성자의 `(PyFrozenSet)self` 하드캐스트를 대체)、`core/PyReprGuard.cs`(신규)、
`PyClassInstance`에 set/frozenset 스토리지 + `NativeStorage()` 하나로 repr/len/in/iter/hash/truthiness/
비교/연산자 슬롯을 모두 라우팅、list/dict/set/frozenset `__new__` 과 set `__init__` 등록、frozenset
메서드 테이블 신설(`// TODO` 였다)、17개 set 디스크립터 self-check、`TpName()`、
`regListBin(..., returnsSelf:)`.

검증 — bughunt **84/84**(`p79_container_subclasses.py` 137줄 diff 0)、evaluate 225/3/2、
Godot 리플레이 82 matched 0 other、perf A/B 총합 **1.00x**(repr_list 1.00x、list_concat 1.01x).
자세한 측정표는 [컨테이너 서브클래스 슬롯](reports/2026-10-30/container_subclass_slots.md).

## P0-42 다중 상속 레이아웃 충돌과 in-place 다이렉트 호출 (측정 2026-10-30, 미해결)

1. `class M(list, dict)` — CPython은 `TypeError: multiple bases have instance lay-out conflict`, 
   SharpPy는 받아들인다. 기준은 CPython의 best-base 계산인데 `class E(str, int)`(IntEnum)는
   합법이어야 하므로 "두 타입 비교"로 대신할 수 없다. 별도 커밋.
2. `s.__ior__([2])`를 **직접** 호출하면 CPython은 `NotImplemented`를 돌려주지만 SharpPy는
   `unsupported operand type(s) for |=: 'S' and 'list'` 를 던진다. 의도적 선택: 테스트는 공유하고, 
   실제 코드가 쓰는 연산자 형태(`s |= [2]`)의 문구가 `|=` 를 유지하는 쪽을 택했다.
3. 이 생성들의 traceback 캐럿 길이(`^^^^^^` vs `^`)차이는 P0-34 cohort 3(칼럼 좌표)소속이다.

## P0-41 stdin 스크립트 실행과 usage 진단 (측정 2026-10-30, 미해결)

`tools/argv_matrix.py`가 등록한 세 가지. P0-39를 닫을 때 함께 고치지 **않고** 남긴 항목이다.

1. `python - one` — CPython은 stdin을 스크립트로 실행하고 `sys.argv == ['-','one']`. SharpPy는
   REPL을 시작한다.
2. 인자 없이 실행(`python < file`) — CPython은 조용히 실행한다. SharpPy REPL은 배너와 `>>> `를
   출력해 stdout이 달라진다. 다만 그 안에서 `sys.argv == ['']` 가 정확한 것은 직접 확인했다.
3. `python -c`(피연산자 없음) — CPython은 `Argument expected for the -c option` + usage 후 **exit 2**,
   SharpPy는 REPL 시작 후 exit 0. usage 문자열은 프로그램 이름을 포함하므로 별도 결정 사항.

SharpPy가 아는 플래그 집합도 CPython보다 좁다(`-u`, `-O`, `-B` 등은 unrecognized이라 거부되지 않고
건너뛴다). `core/CommandLineParser.cs`는 **위치지키 규칙**만 미러한다는 주석이 있다.
`sys.executable`, `-m` 실행(`Program.cs`에서 분기가 아직 주석)은 이번 범위 밖.

## P0-39 `sys.argv`가 비어 있다 → 닫힘 (2026-10-30)

`.artifacts/tmp/lockcase/argvchk.py`를 `script.py one two`로 실행:

- CPython 3.12.15: `3` / `['.artifacts/tmp/lockcase/argvchk.py', 'one', 'two']`
- SharpPy: `0` / `[]`

스크립트 경로 자체가 들어가지 않는다. 인자를 읽는 모든 스크립트가 깨지므로 A방(결정적 결과)
실패이며, 타이밍과 무관하게 즉시 고칠 수 있는 종류다. 관련해서 `Program.cs`는 `-c`를 파싱만
하고 `ExecuteCodeString` 호출이 주석 처리되어 있어 `sharppy.dll -c "print(123)"`가 아무것도
출력하지 않고 0으로 끝났다(같은 절의 문제).

**해결**: `core/PyArgv.cs`(`PyConfig.argv`의 대응) + `core/CommandLineParser.cs`(각할 규칙) +
`Program.cs`의 `-c` 실행 활성화 + `modules/SysModule.cs`의 `argv` 생성.
기관은 CPython과 같다: `sys.argv`는 `PyConfig.argv` 복사(`Python/sysmodule.c:3612`)이고,
`config_update_argv`(`Python/initconfig.c:2723`)가 첫 non-option부터 슬라이스하고 비면 `L""`을
넣으며 `-c`/`-m`은 `argv[0]`을 강제로 바꾼다. SharpPy도 같은 세 규칙을 그대로 구현했다.

검증 — `tools/argv_matrix.py`: 스크립트+인자 / 무인자 / 인터프리터 플래그 선행 / 역슬래시 경로 /
`-c` / `-c`+인자 / `-c ""` **7/7 일치**. godot 리플레이 82 matched, 0 other (임베딩에서는
`PyArgv`가 빈 기본값 — 인자를 설정하지 않는 임베딩의 CPython과 동일).
자세한 측정값과 남은 차이는 [sys.argv와 명령 모드 실행](reports/2026-10-30/sys_argv_and_command_mode.md),
남은 항목은 위의 **P0-41**.

## P0-43 tp_flags 층 부재 → 닫힘 (2026-10-30)

CPython은 "이 객체가 무슨 종류인가"를 항상 타입의 비트 하나로 답한다 — `PyList_Check(op)`는
`ob_type`에 대한 feature test(`Include/listobject.h:24`)이고 테스트 자체는 두 명령
(`Include/object.h:959`)이다. SharpPy에는 이 층이 **전혀** 없었다(`core/PyType.cs:432`에
"TypeKind가 tp_flags 대응"이라는 주석만). 그래서 `self is PyList` 식의 CLR 검사 34곳과
스토리지 API 참조 124곳이 site마다 기관을 다시 조합하는 형태였다.

측정이 드러난 결함 두 개(모두 room A):

1. `class C(bool)` — CPython `TypeError: type 'bool' is not an acceptable base type`
   (`Objects/typeobject.c:2789`), SharpPy는 클래스를 만들었다. `NoneType`, `slice`도 같다.
   정적 스펙은 BaseType을 **상속하지** 않는다(`PyBool_Type`의 `tp_flags`는 DEFAULT뿐，
   `Objects/boolobject.c:190`) — heap 타입만 base 플래시를 복사한다（`class C(list)`는
   `LIST_SUBCLASS`와 `SEQUENCE`를 모두 가짐，실측）.
2. `type('C', (5,), {})` — CPython은 base의 타입을 metaclass로 끌어 `_PyType_CalculateMetaclass`
   에서 충돌을 낸다(`Objects/typeobject.c:3083`). SharpPy는 `core/PyTypeMetaclass.cs:519`의 하드
   캐스트가 CLR `InvalidCastException`을 그대로 흘렸다(`RuntimeError` + C# 스택). base 4종
   (int/인스턴스/`len`/`None`) 모두 같은 충돌 메시지라 검사도 균일로 넣었다.

구현: `core/PyTypeFlags.cs`(비트값을 `Include/object.h`에서 그대로 옮기고 라인별 앵커)、
`PyType.Flags/HasFeature/ComputeFlags`(스택은 spec 행 + `inherit_special` 전파，heap은 base 복사)、
`IsListSubclass/IsDictSubclass/IsStrSubclass` 플래시 판독(호출마다 MRO 스캔 5개 삭제)，
`IsSetSubclass/IsFrozenSetSubclass`는 MRO 유지 — CPython도 저 둘에 플래시가 없고
`PyType_IsSubtype`를 쓴다(`Include/setobject.h:36`, `:30`).

검증 — bughunt **85/85**(`p80_type_flags.py` 89줄 diff 0: 거부 3종、비타입 base 충돌 4종、
3단계 list 서브클래스、2단계 dict/set 서브클래스、frozenset hash、metaclass 정상)、
evaluate 225/3/2、Godot 리플레이 82/0、`tools/cpython_similarity.py` 종료코드 0.

다만 **모양 지표는 거의 움직이지 않았다**(스토리지 참조 124→126). 기관이 되는 층을 넣는 일과
불필요한 표현을 지우는 일은 다르기 때문에, 나머지는 아래 P0-43b로 남긴다. 전체 해석은
[type_flags와 허용되지 않는 base](reports/2026-10-30/type_flags_and_unacceptable_bases.md).

## P0-43b 컨테이너 서브클래스의 표현이 CPython과 다르다 (측정 2026-10-30, 미해결)

같은 파이썬 개념(내장 컨테이너의 서브클래스 인스턴스)에 대해 트리에 **4가지 표현**이 공존한다:
`PyTupleSubclass : PyTuple`/`PyBytesSubclass : PyBytes`(CLR 상속 — CPython과 동일 형태)、
`PyIntSubclass : PyObject` 류(값을 감쌈)、`PyClassInstance` + `_list/_dict/_set/_frozenStorage`
(CPython에 없는 객체 하나 더)、`PyObject._customType`(`ob_type` 오버라이드의 올바른 대응).
sealed인 타입은 없으므로 C# 단일 상속은 이를 강요하지 않았고, CPython도 단일 best_base라
같은 결론이다. 수렴 순서: 컨테이너를 CLR 상속 래퍼로 옮겨 `x is PyList`가 곧 `PyList_Check`가
되게 하고, `PyNativeStorage`는 캐스트만 남기고, `PyClassInstance`는 CPython이 주는 일(`__dict__
를 가진 heap type`)만 지킨다. 선행 필요: `is PyClassInstance`로 서브클래스를 구분하는 지점
(`core/PyBuiltin.cs:3776`, `core/PyType.cs:2882` 등) 함께 처리.

**Q1 완료 (2026-10-31)** — 서브클래스 인스턴스는 그 타입 자신의 tp_new가 만든다(CPython에 중앙
팩토리는 없다). `float`에는 `__new__` 슬롯 자체가 없어서 `super().__new__(cls, v)`가
`object.__new__`로 이어지며 `TypeError: object.__new__() takes exactly one argument`를 냈다
(실측: CPython `D(2.0)` → `4.0`). 이제 `type/PyFloat.cs:48 FloatNew`가
`float_new`/`float_subtype_new`(`floatobject.c:1644`, `:1677`, arity는 `:1705`)의 단일 구현이고,
제너릭 코드에 있던 계열 분기를 삭제했다: `PyClass.CreateInstanceCore`의 float 분기、
`PyType.Call`의 bytes 서브클래스 분기、`PyClass.CreateInstanceCore`의 bytes 분기、그리고
`PyType.Call`의 float 분기는 `FloatNew`로 위임(bytes는 `type/PyBytes.cs:210`이 기관)。
측정 — `family branches in generic paths 7→3`, `*_subtype_new sites 7→3`(사를 있던 float 분기와 FloatSubtypeNew는 제거 후도 bughunt 86/86으로 살아있음을 증명)、bughunt **86/86**
(`p81_float_new_and_super.py`: float 행렬 + 5개 payload base 전체의 `super().__new__` + 거부 3종)、
evaluate 225/3/2、Godot 82/0、perf A/B 0.95x~1.03x(노이즈)。
자세히는 [서브클래스 인스턴스는 그 타입이 만든다](reports/2026-10-31/subtype_new_belongs_to_the_type.md).

**Q2 부분 완료 (2026-10-31)** — `list` 계열은 CLR 서브타입으로 수렴했다. `PyListSubclass : PyList`
가 그 자리를 대신하고, `_listStorage`·생성 시 할당·`GetListStorage()`·`PyNativeStorage.ForList`의
3단계 특례를 삭제했다(지금은 `self as PyList` 한 줄). 도중에 `evaluate`가
`tests/warnings_boundary_regression.py`를 실패로 잡았는데, CPython에는 길이로 가는 두 갈래가
공존하기 때문이다: `len(x)`는 `sq_length` 슬롯이라 서브클래스가 선언한 `__len__`이 이기지만,
`list.__len__(x)`는 `list_length`라 layout을 직접 읽는다(`_warnings`가 필터 스캔에 쓰는 것).
그래서 `RawCount`/`RawIterator`/`RawContains`(virtual 아님)를 두고 슬롯 쪽이 거기에 위임한다 —
일은 한 번 구현되고 dispatch만 두 갈래다(`type/PyList.cs`). 선언된 dunder가 이기는 규칙
(`update_one_slot`)은 네 컨테이너 서브클래스가 재발명하지 않게 `SubclassInstanceSupport`에 담았다.
남은 저장소: `_dictStorage`/`_setStorage`/`_frozenStorage`/`_strStorage` — 타입별로 두 갈래를
먼저 나눈 뒤 같은 방식으로 지운다. 게이트: bughunt **86/86**, evaluate `225/3/2`, Godot 82/0,
perf `list_comp` baseline 대비 -2.3%. 상세는
[list subclass is the layout](reports/2026-10-31/list_subclass_is_the_layout.md).

남은 branch: `core/PyType.cs:1004`/`:1010`(str — `str.__new__`는 이미 존재
`type/PyString.cs:1625`)、`:975`의 bytes-like encoding 검사. 그리고 `float.__new__` repr이
`<builtin_function_or_method object at 0x…>`(CPython은 `<built-in method __new__ of type object …>`)。

## P0-44 class 문장에서 타입이 아닌 base (측정 2026-10-30, 미해결)

P0-43은 `type('C', (b,), {})` 경로만 고쳤다. `class C(b): pass`는 다른 기관(계산된 metaclass를
**호출**)을 쓰기 때문에 메시지가 다르며, SharpPy는 지금 클래스를 만들어 버린다:

| base | CPython | SharpPy |
|---|---|---|
| `5` | `TypeError: int() takes at most 2 arguments (3 given)` | 생성 성공 |
| 인스턴스 | `TypeError: Inst() takes no arguments` | 생성 성공 |
| `len` | `TypeError: cannot create 'builtin_function_or_method' instances`(`tp_new`가 NULL) | 생성 성공 |
| `None` | `TypeError: NoneType takes no arguments` | 생성 성공 |

## P0-45 내장 타입 표면 결함 (측정 2026-10-30, 미해결)

`tools/cpython_similarity.py`가 CPython 타입 스펙(`/* tp_repr */` 주석 기준으로 positional 파싱)과
비교해 찾은 실제 결핍: `set.__reduce__`, `frozenset.__reduce__`(pickle 구멍), `set.__sizeof__`,
`frozenset.__sizeof__`, `dict.__sizeof__`, `str.__getnewargs__`.
도구 한계도 명시: `__eq__`/`__iter__`는 CLR virtual(`RichCompare`, `GetIterator`)이라 TypeDict
등록으로 세지져 "missing"으로 표시되고, `set.test_c_api`는 CPython 자체 테스트 훅이다.

## P0-46 등록 위치가 CPython의 타입별 스펙과 어긋난다 (측정 2026-10-30, 미해결)

CPython은 타입마다 자기 스펙을 같은 .c 파일에 둔다(`set_methods`는 `Objects/setobject.c`).
SharpPy는 set/frozenset의 표면을 자기 파일에 하나도 등록하지 않고 `core/PyType.cs` 중앙
테이블(53항목)에 둔다(도구 M3의 own-file 열: set 0, frozenset 0, list 22, str 56).
파일 단위로 옮기면 스펙 비교가 라인 단위로 가능해진다.

