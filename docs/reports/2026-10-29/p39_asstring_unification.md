# p39 — `AsString()` becomes the single Python-`str()` virtual (naming unification)

Date: 2026-10-29
Branch: `work/bug-hunt`
Follows: p37 (`AsString()` → `ToClrString()`), p36 (`To~`/`As~` convention)

## Goal (user request)

Make the conversion API internally consistent: `AsInt/AsFloat/AsBool` return Python
types, so the Python-`str()` conversion should be `AsString()`, with `ToStr()` kept as a
compatibility shim for existing call sites, and `ToClrString()` kept for the C# string.

## Measured scope before changing anything

| item | count |
|---|---|
| `virtual PyString ToStr()` definitions | **1** (`core/PyObject.cs`) |
| `override PyString ToStr()` | **41** across 28 files |
| `new`-hiding `ToStr` | 0 |
| reflection / `nameof` / `"ToStr"` literals | 0 |

0 `new` hiding + 1 base virtual is what makes this safe: making the shim **non-virtual**
turns any missed override into `CS0115`, so the compiler proves completeness.

## Changes

1. `core/PyObject.cs`
   - `ToStr()` body → `public virtual PyString AsString()`.
   - `public PyString ToStr() => AsString();` — **non-virtual**, so a subclass that still
     overrides `ToStr` cannot compile; no `ToStr`/`AsString` double path.
   - `ToClrString() => AsString().Value`.
2. 41 `override PyString ToStr()` → `override PyString AsString()`; 3 `base.ToStr()` →
   `base.AsString()`; 7 unqualified `ToStr()` calls inside overriding classes unified to
   `AsString()`, so **zero unqualified `ToStr()` calls remain** (the recursion hazard).
   136 qualified `x.ToStr()` call sites still use the alias, including two delegations to
   other objects (`PyAsyncGenerator.AsString() => Value.ToStr()`,
   `PyIntSubclass.AsString() => _intValue.ToStr()`), which are correct because they do
   not call through themselves.
3. Recursion audit: every unqualified self-call resolved to a class that overrides
   `AsString()`, so the shim cannot self-loop.

## Bug found by the new probe (P0, fixed)

`tests/bughunt/p39_str_dispatch.py` compares 111 lines of `str()`/`repr()`/f-string/`%s`
across 25 object kinds and immediately exposed exception swallowing in both
`PyObject.AsString()` and `PyClassInstance.AsString()`, plus `PyClassInstance.ToRepr()`:
a blanket `try { … } catch { }` fell through to the repr/identity form.

Measured CPython (`PyObject_Str` / `PyObject_Repr`) and now implemented:

| case | CPython 3.12.15 | SharpPy before |
|---|---|---|
| `str(x)` where `__str__` raises | `ValueError: boom` propagates | `'fallback-repr'` (swallowed) |
| f-string / `format()` of same | propagates | swallowed |
| subclass of a raising `__str__` | propagates | swallowed |
| `__str__` returns non-string | `TypeError: __str__ returned non-string (type int)` | repr fallback |
| `__str__ = None` | `TypeError: 'NoneType' object is not callable` | repr fallback |
| `str(x)` where `__repr__` raises | propagates | `<… object at 0x…>` |
| `__repr__` returns non-string | `TypeError: __repr__ returned non-string (type int)` | identity form |

Rule now encoded: **fall back to `__repr__` only when the special method is absent**;
exceptions propagate; non-string results raise the CPython `TypeError`.

## Verification

- `tests/bughunt/p39_str_dispatch.py`: 111 lines, stdout diff `0` (addresses normalized).
- str-semantics micro matrix: diff `0` except the metaclass case below.
- `tools/run_bughunt.py`: **44/44 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: `str_fmt +0.3%`, no `REGRESSION` — the shim costs nothing
  measurable (non-virtual, inlined).
- Godot 4.7.2 mono replay, Release DLL: `REPLAY_RESULT 82 matched, 0 other`.

## Residuals registered

- **P0-12**: metaclass `__str__`/`__repr__` is not consulted for class objects —
  `str(C)` gives `<class '__main__.C'>` where CPython gives `'META'`.
- **P1**: generator `repr()` lacks the CPython qualname chain.
- Instance-level `__str__` assignment is still honored by `PyClassInstance`, whereas
  CPython looks special methods up on the type. Left alone deliberately this round.
- `ToStr()` alias remains for ~130 legacy call sites; removing it is a pure sweep.
