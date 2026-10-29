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
- **P0-19 (new, reliable reproducer): a caught exception escapes later in a long probe file.**
  Three independent observations, always after roughly twenty handled exceptions in one file, and
  the failing case *moves* when an earlier case is removed:
  * appending the float cases to `tests/bughunt/p49_subclass_gates.py` -> unhandled `OverflowError`
    attributed to `FSub(10 ** 400)` (CPython prints the same caught line and then continues);
  * removing that case moved the escape to the final `x.no_such`;
  * appending the round/hash cases moved it to `int.__round__(5, ndigits=2)`.
  Each statement is caught and printed correctly when run in a short file - `tests/bughunt/p50_round_hash_slots.py`
  (32 cases) and `tests/bughunt/p48_float_subclass.py` both pass - so this is state accumulated by
  handled exceptions, not the statement itself. Same area as the p17 `PyFrame.HandledScopes` fix.
  Workaround used, not a fix: keep probe files under ~25 caught-exception cases and split instead of
  appending. Do not read the 55/55 pass as covering P0-19.
- Unexplained, recorded rather than smoothed over: while extending the P0-18 probe with the float
  cases, the combined probe made **both** engines abort on `FSub(10 ** 400)` (CPython printed the
  same `EXC OverflowError ...` line first, then its own traceback), and after removing that case
  SharpPy additionally emitted an unhandled traceback at the last case (`x.no_such`) although the
  identical statement is caught and printed in `tests/bughunt/p48_float_subclass.py`. Same source
  line, two outcomes depending on surrounding file state. That is a candidate real defect in
  exception-state handling, but I do not have a minimal reproducer yet, so it is not numbered as a
  P0; the float probe extension is deferred until it does. Do not read the 54/54 bughunt pass as
  covering this.
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
