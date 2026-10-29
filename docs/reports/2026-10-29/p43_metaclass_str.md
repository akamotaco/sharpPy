# p43 — metaclass `__str__` / `__repr__` for class objects

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-12

## Measured divergence

CPython dispatches `PyObject_Str` / `PyObject_Repr` on `type(obj)`, and for a class object
`type(obj)` is the **metaclass**. SharpPy built the class repr directly and never asked the
metaclass:

| call | CPython | SharpPy before |
|---|---|---|
| `str(C)` / `repr(C)` (`class M(type): __str__/__repr__`) | `'META-str:C'` / `'META-repr:C'` | `<class '__main__.C'>` |
| `f"{C}"`, `format(C)`, `"%s" % C` | `'META-str:C'` | `<class 'C'>` (module prefix also lost) |
| `format(C, ">20")` | `TypeError: unsupported format string passed to M.__format__` | returned `<class 'C'>` |
| `[C]`, `{C: 1}`, `(C,)` | metaclass `__repr__` inside containers | default class repr |
| `str(D)` (`class D(C)`), `str(E)` (`class M2(M)`) | metaclass inherited | default |
| metaclass `__str__` returns `5` | `TypeError: __str__ returned non-string (type int)` | silently default repr |
| metaclass `__str__` raises | exception propagates | swallowed |
| `type.__str__(C)` | `'META-repr:C'` (CPython has no `type.__str__`; it is `object.__str__`, which calls `tp_repr`) | `<class 'C'>` |
| `type.__repr__(C)` | `<class '__main__.C'>` (static `type_repr`, no metaclass) | same ✓ |
| `str(G)` (`class M3(type)` with no specials), `str(Plain)`, `str(type)`, `str(int)` | default `<class ...>` | ✓ already correct |

## Implementation

- `core/PyType.cs`: `ToRepr()` / `AsString()` now call a new `MetaSpecial(name)` helper that
  walks the **metaclass MRO** and stops at `type` / `object` — those slots *are* the default
  behaviour `TypeRepr()` implements, so continuing would re-enter it. A metaclass entry is
  invoked with the class object as the single argument (descriptor-bound when the entry is a
  descriptor, explicit `self` for a plain `def __str__(cls)`), and a non-`str` result raises
  the p39 `__str__ returned non-string (type X)` / `__repr__ ...` guard; exceptions propagate.
  For the ordinary case (metaclass is exactly `type`) the walk breaks on the first entry, so
  no cost is added to normal reprs.
- CPython has **neither** `type.__str__` **nor** `type.__format__`; both resolve to `object`'s
  versions. Fixed in both places the descriptors existed (`core/PyType.cs` TypeDict and the
  heap-`type` ClassDict in `core/PyTypeMetaclass.cs`, which is the one attribute lookup
  actually reaches):
  - `type.__str__` → dynamic `ToRepr()` (hence `'META-repr:C'`),
  - `__format__` → empty spec returns `str(self)`; a non-empty spec raises
    `unsupported format string passed to {metaclass}.__format__`, and `object.__format__`
    now names `type(obj)` too instead of the class itself.

## Verification

- `tests/bughunt/p43_metaclass_str.py`: 37 lines, stdout diff **0**, with in-probe address
  normalization (the first harness run flagged a mismatch purely from instance identity
  addresses, which the probe now normalizes like p23/p39).
- `tools/run_bughunt.py`: **48/48 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no workload over the 15 % flag (max `comp_lambda +6.0 %`).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Deferred (registered)

- **P1-9**: `C.__str__()` on a class gives `__str__() missing required 'self' argument`,
  where CPython finds `object.__str__` before the metaclass slot and reports
  `descriptor '__str__' of 'object' object needs an argument`. Prerequisite is the
  `object` protocol materialization from P1-1 (naively exposing `object.__str__` broke repr
  dispatch in p22), so it needs the dispatch bridge rather than another per-type entry.
