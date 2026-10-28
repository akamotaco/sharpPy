# p31 — `re` named groups + findall single-group shape

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Feature check → `re` gaps

A regex matrix surfaced divergences. The engine is a **hybrid**: SharpPy ships
the pure-Python CPython `Lib/re` package (`_parser.py`, `_compiler.py`) for the
full `re` API, and a native `_sre` backend (`modules/_SreModule.cs`) built on
`System.Text.RegularExpressions`. Some entry points (`re.match` / `re.search`)
compile the **raw** pattern through the native backend.

Fixed here:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `re.match(r"(?P<y>\d{4})", "2024").group("y")` | `'2024'` | `ValueError: … Unrecognized grouping construct` (.NET `(?P<` unsupported) |
| `re.match(r"(?P<y>\d{4})", "2024").groupdict()` | `{'y': '2024'}` | same error |
| `re.match(r"(\d)(?P<b>\w)", "1a").group("b")` | `'a'` | same error |
| `re.findall(r"(\w) \1", "a a b c c")` | `['a', 'c']` | `[('a',), ('c',)]` (single group wrapped in a 1-tuple) |

## Fix

`modules/_SreModule.cs`:
- `ToDotNetPattern(string)` translates the two Python-only constructs the .NET
  engine needs before compilation:
  - `(?P<name>` → `(?<name>`
  - `(?P=name)` → `\k<name>`
  Fast path returns the pattern unchanged when `(?P` is absent, so ordinary
  patterns are untouched. Applied at the native `new Regex` entry points
  (`Match`, `Search`, and `PySrePattern`); `.pattern` still reports the original
  Python text (translation is only for compilation).
- `PySrePattern.FindAll` return shape now matches CPython: 0 groups → whole
  match; exactly 1 group → that group's string (was a 1-tuple); 2+ groups →
  tuple of groups `1..n`.

`group(name)` and `groupdict()` already resolved names through
`Match.Groups[...]`, so they work once the pattern compiles.

## Verification

- `tests/bughunt/p31_re_named.py`: stdout diff `0` vs CPython (named group,
  groupdict, mixed positional+named, `re.compile(...).match().group(name)`,
  .NET `(?<name>` passthrough, findall 0/1/2-group shapes, `finditer`).
- `tools/run_bughunt.py`: **38/38 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION`.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residual `re` gaps (deferred — engine routing is hybrid, not clean)

- **Named backreference `(?P=name)`**: `re.findall(r"(?P<c>\w) (?P=c)", …)`
  returns `[]`. The measured `(?P<c>` path goes through the pure-Python
  `Lib/re/_parser`, and the native `\k<c>` backreference to a translated named
  group does not reproduce CPython's match. Needs a decision on whether to make
  the native engine authoritative for backrefs or fix the pure-Python compile
  path; deferred rather than patch one entry point inconsistently.
- **Any non-zero `flags=` argument** (e.g. `re.IGNORECASE`) crashes with
  `AttributeError: 'memoryview' object has no attribute 'cast'` because
  `Lib/re/_compiler.py:398` does `memoryview(b).cast('I')` to build the code
  array, and SharpPy's `memoryview` lacks `.cast`. This blocks the whole flags
  path, not just IGNORECASE. Either implement `memoryview.cast` or bypass the
  pure-Python compiler for the native backend; deferred as a distinct cluster.
