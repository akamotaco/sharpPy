# p66 — builtin functions and keyword arguments (P0-26)

Date: 2026-10-30 · branch `work/bughunt` · reference CPython 3.12.15

## What was wrong

Keyword handling had been fixed for methods (P0-20) and for constructors (P0-25), but module-level
builtin functions still disagreed with CPython, and the failure mode was worse than a wrong message:

```text
abs(x=-5)     CPython: abs() takes no keyword arguments
              before:  abs() takes exactly one argument (0 given)
hasattr(obj=1, name="x")
              CPython: hasattr() takes no keyword arguments
              before:  hasattr() takes exactly 2 arguments (0 given)
```

The keyword was dropped, and the interpreter then complained about the argument that keyword was
supposed to supply. A call that had to be refused was reported as a different call.

Separately, keywords CPython *accepts* were silently ignored rather than bound:
`pow(2, 3, mod=5)` returned `8`, i.e. `pow` without the modulus.

## The table was generated, not typed

Cases came from CPython's own `__text_signature__` values
(`.artifacts/tmp/gen_builtin_kw_table.py` → `builtin_kw_table.json`), so the parameter names are the
interpreter's, not guesses. The rule that has to be read correctly: everything before the first bare
`/` is positional-only, **and** a parameter carrying its own trailing `/` is positional-only even
after that slash. That is why `sorted(dict, key=len)` works while `sorted(iterable=[1])` is refused
with `sorted expected 1 argument, got 0`.

**32 builtins take no keyword at all** — abs, aiter, all, anext, any, ascii, bin, callable, chr,
delattr, dir, divmod, eval, format, getattr, globals, hasattr, hash, hex, id, input, isinstance,
issubclass, iter, len, locals, next, oct, ord, repr, setattr, vars — and all 32 use one text,
`name() takes no keyword arguments`. That is a single guard in `PyBuiltinFunction.Call`, before any
dispatch path.

**Builtins that do take names** are bound by name now: `round(number, ndigits)`,
`pow(base, exp, mod)`, `print(*args, sep, end, file, flush)`, `sorted(*, key, reverse)`,
`sum(iterable, start)`, `min/max(*, key, default)`, `enumerate(iterable, start)`,
`zip(*iterables, strict)`, plus `exec(*, closure)`, `compile(...)`, `__import__(name, ...)`.

## Per-function differences that measurement, not a rule, settled

| fact | evidence |
|---|---|
| sorted's unknown-name text says `sort()` | `sorted([1], zzz=1)` → `'zzz' is an invalid keyword argument for sort()` — it shares `list.sort`'s parser |
| some functions check arity before keywords | `min(zzz=1)` → `min expected at least 1 argument, got 0` |
| others check keywords first | `enumerate(zzz=1)` → `'zzz' is an invalid keyword argument for enumerate()` |
| a missing required parameter names its position | `pow()` → `pow() missing required argument 'base' (pos 1)`; `round()` → same for `number` |
| `enumerate` is **not** positional-only | `list(enumerate(iterable=[7])) == [(0, 7)]`. The ledger had this right and the reason wrong: its signature is `(iterable, start=0)` with no `/` at all |
| `reversed` **is** positional-only | `reversed(sequence=[1])` → `reversed() takes no keyword arguments` |
| `map`/`filter`/`range` also refuse | `map(function=None, iterable=[1])` → `map() takes no keyword arguments` |

Two of these changed behaviour rather than wording: `pow(..., mod=)` now applies the modulus, and
`enumerate(iterable=...)` / `reversed(...)` no longer reject or accept the wrong thing.

## Gates

* `tests/bughunt/p66_builtin_keywords.py`: 63 generated keyword cases + 35 covering named arguments,
  unknown names and check order. CPython vs SharpPy diff **0**.
* `tools/run_bughunt.py`: **71/71**. `tools/evaluate.py`: unchanged
  `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
* Godot 4.7.2 mono (source-included Release build, 0 errors): replay `82 matched, 0 other`.
* `tools/perf_probe.py`: no flag (all workloads +0…9%, inside the band already documented as noise).
  The guard runs only when `kwargs != null`, and the extra named-argument reads only cost on calls
  that pass keywords.

## What this deliberately left open — P0-29

* The **positional** arity texts: 11 of 15 sampled builtins still invent a form that matches none of
  CPython's three families: `any()` should say `any() takes exactly one argument (0 given)` but says
  `any expected exactly 1 arguments (0 given)`, and `divmod(7)` should say
  `divmod expected 2 arguments, got 1`. p66 pins the builtins this commit touched (`reversed`,
  `enumerate`, `pow`, `round`, `sorted`, `sum`); the remaining ones need the same table-driven sweep.
* `getattr`/`hasattr` swallow user exceptions: `getattr(Boom(), "anything", "D")` returns `'D'` where
  CPython propagates `ValueError` (CPython catches AttributeError only), and there are two
  implementations of each (`modules/BuiltinsModule.cs` and `core/PyBuiltin.cs`), one of which carries
  an extra `result is PyNone → use default` escape hatch.
