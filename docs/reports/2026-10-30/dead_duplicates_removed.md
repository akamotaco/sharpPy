# p70 — dead duplicates removed: builtin table, culture-aware string helpers, exception names

Date: 2026-10-30 · branch `work/bug-hunt` · reference CPython 3.12.15

Three items from the duplicate-implementation audit. All three were "the same operation exists
twice, and the copy that runs is not the copy that was maintained".

## 1. Builtin table entries behind a registration delegate

The scanner flagged 13 names. Eleven were real duplicates:
`abs, ascii, bin, format, hex, isinstance, issubclass, len, memoryview, oct, vars`.
`compile` and `pow` were **false positives** — the scanner matches by name, and `_sre.compile` /
`operator.pow` are unrelated module attributes; both builtins were already registered through the
table only. Being wrong in the safe direction is still being wrong, so the count is corrected here.

`BuiltinsModule` registers e.g. `new PyBuiltinFunction("len", Len)`, and `PyBuiltinFunction.Call`
prefers the delegate, so the `_builtinImplementations["len"]` entry never ran. The dead copies were
not merely unused, they were the **stale** ones:

* `CallBin` / `CallHex` / `CallOct` still used `obj is PyInt` exact tests, i.e. the behaviour from
  before `PyObject.IndexOrThrow` centralised `__index__`.
* `CallIsInstance` printed `[isinstance] ...` to stdout unconditionally. Had it ever been reachable,
  every `isinstance` call in the test corpus would have polluted output.
* `CallMemoryview` had an `is`-chain over buffer-capable types, predating the buffer-protocol work.

Removed: 11 registrations + 11 method bodies = **361 lines**.

Proof that they were dead rather than merely unread: a 60-case probe covering all eleven builtins
(values, `__index__`/`__len__` hooks, error texts, keywords) produced **byte-identical output before
and after** the deletion, except the three lines listed at the end of this report.

## 2. Uncalled `PyString.Upper()`, `Lower()`, `Capitalize()`

Zero call sites in `core/ type/ modules/ runtime/ parser/`. They used `Value.ToUpper()`,
`Value.ToLower()` and `CultureInfo.CurrentCulture.TextInfo.ToTitleCase(...)` — culture-aware helpers,
which is exactly what the live `title` implementation deliberately abandoned for CPython's cased
character rule (`"it's" -> "It'S"`). Deleted. This is the kind of code that gets "fixed" back into
use by someone who doesn't know why nothing calls it.

## 3. Exception matching: one rule instead of three name comparisons

Three places decided whether a handler catches an exception:

1. `PyVM` `CHECK_EXC_MATCH` — a forest: natives went through a *name → CLR type* map and
   `IsAssignableFrom`, wrapped user exceptions through `OriginalClass`, `PyClassInstance` through
  `InstanceType`, with name-equality fallbacks;
2. `PyVM.ExceptionMatches` (`except*` path) — `excTypeName.Equals(targetTypeName,
   StringComparison.OrdinalIgnoreCase)` plus a hardcoded "Exception matches everything";
3. `PyBaseExceptionGroup.ExceptionMatches` — compared `"PyValueError"` against `"ValueError"`.

All three are replaced by one matcher, `PyVM.ExceptionMatchesHandler`, which is CPython's
`CHECK_EXC_MATCH` → `PyObject_IsInstance`:

* a tuple handler matches when any element matches;
* a handler that is not an exception class raises
  `catching classes that do not inherit from BaseException is not allowed` — **even when the
  exception would otherwise escape the `try`**;
* otherwise the raised object's own type is walked through its MRO by reference.

The identity walk was only safe after measuring it: `type(ValueError('x')) is ValueError` is True,
`type(MyErr('x')) is MyErr` is True, and `ValueError in type(SubValue('x')).__mro__` is True. With
that, no name comparison is needed anywhere — and one was actively harmful:
`ExceptionClassMatches` ended with `m.Name == expected.Name`, so two unrelated classes that happen to
share a name caught each other. p70 pins that:

```python
Sibling = make_sibling()          # a second class named MyErr
except Sibling: ...               # must NOT catch the original MyErr
```

Removed: 89 lines (the forest plus the case-insensitive matcher) and 12 lines (the group's copy).

## What fell out of the same area

`vars` was the one builtin whose live delegate was weaker than the copy being deleted, so it is now
correct instead of merely consolidated:

| call | CPython | before | after |
|---|---|---|---|
| `vars()` | `locals()` | `NotImplementedError` | the same dict `locals()` returns |
| `vars(1)` | `TypeError: vars() argument must have __dict__ attribute` | `AttributeError: 'int' object has no attribute '__dict__'` | matches |
| `vars(1, 2)` | `vars expected at most 1 argument, got 2` | `vars() takes at most 1 argument (2 given)` | matches |
| `format()` | `format expected at least 1 argument, got 0` | `format() takes 1 or 2 arguments (0 given)` | matches |

`vars()` consolidating onto `locals()` also exposed three gaps that are now registered rather than
papered over: module-level `locals()` must be the module dict (`locals() is locals()` is False
today), `vars(cls)` key sets differ (CPython `['__dict__','__doc__','__module__','__weakref__']`),
and `iter(vars(int))` fails because mappingproxy has no `__iter__`.

## Registered, not fixed

**P0-33 — `except*` / `ExceptionGroup` do not follow PEP 654.** Routing `except*` through the new
matcher showed that the remaining problem is structural, not about matching: groups store
`List<PyException>`, so a user-defined exception cannot enter a group at all
(`ExceptionGroup("g", [MyErr("a")])` → `TypeError: second argument (exceptions) must be a non-empty
sequence`), `PrepReraiseStarExceptions` casts items with `(PyException)item` and dies with
`InvalidCastException` on `PyNone`, and `except* ValueError` over a native group is not caught. The
reproducers are in the ledger and deliberately **not** in the probe: pinning them would freeze wrong
behaviour.

## Gates

* `tests/bughunt/p70_exception_matching.py` — 55 cases, diff **0**; `tools/run_bughunt.py` **75/75**.
* `tools/evaluate.py`: unchanged `225 / 3 / 2`.
* Godot 4.7.2 mono: 0 errors, `SHARPPY_PATH_CHECKS 5`, `REPLAY_RESULT 82 matched, 0 other`.
* `tools/perf_probe.py`: no workload above the flag threshold.
* Because the changed path is exception matching, an interleaved A/B against `d212fe8` was run on the
  exception-heavy suite (7 workloads × 20 000 iterations): every workload −2.0%…+0.1%, **TOTAL −1.4%**.
