# p29 — `map()`/`filter()` reject keywords; `list.index` start/stop

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Feature check → two bounded gaps

A feature matrix (`zip`/`map` `strict=`, `int.bit_count`, `str` 3.9+ methods,
`list.index`/`count`, `set.isdisjoint`, …) showed SharpPy correct except:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `map(str, [1], key=2)` | `TypeError: map() takes no keyword arguments` | silently ignored kwargs |
| `map(str, [1], strict=True)` | `TypeError: map() takes no keyword arguments` | `['1']` (kwargs dropped) |
| `filter(None, [1,0], key=2)` | `TypeError: filter() takes no keyword arguments` | silently ignored kwargs |
| `[1,2,3,2].index(2, 2)` | `3` | `TypeError: index() takes exactly one argument (2 given)` |
| `[1,2,3,1].index(1, 1)` / `index(1, 0, 2)` | `1` / `0` | TypeError (too many args) |
| `[1,2].index()` | `TypeError: index expected at least 1 argument, got 0` | TypeError (other wording) |
| `[1,2].index(1, "a")` | `TypeError: slice indices must be integers or have an __index__ method` | TypeError (other wording) |
| `[1,2].index(1, start=0)` | `TypeError: list.index() takes no keyword arguments` | TypeError (other wording) |

Key measured fact: this CPython 3.12.15 **`map()` accepts no keyword
arguments** (`map() takes no keyword arguments`) even though `zip()` supports
`strict=`. Confirmed `type(builtins.map)` is the real `type` (not shadowed) and
`sys.version` is `3.12.15`. Runtime is authoritative over the assumption that
"map has strict".

## Implementation

- `core/PyBuiltin.cs` `CallMap` / `CallFilter`: reject keyword arguments with
  the CPython messages `map() takes no keyword arguments` /
  `filter() takes no keyword arguments` (checked before the arg-count check).
  `zip` keeps `strict=` support (verified). `map() must have at least two
  arguments.` wording already matched.
- `type/PyList.cs`: `index` descriptor now accepts `(value, start=0, stop=len)`,
  positional-only:
  - kwargs → `list.index() takes no keyword arguments`
  - `args.Length < 1` → `index expected at least 1 argument, got N`
  - `args.Length > 3` → `index expected at most 3 arguments, got N`
  - non-integer start/stop (no `__index__`) → `slice indices must be integers or
    have an __index__ method` (uses `PyObject.TryGetIndex`)
  - negative start/stop adjusted by `+len` and clamped to `[0, len]`, `None`
    stop = full length, matching CPython `list_index`; miss →
    `ValueError: {value!r} is not in list`.
  - Added `PyList.Index(item, start, stop)` overload (same `AreEqual` semantics
    as the existing single-arg `Index`).

## Verification

- New probe `tests/bughunt/p29_map_listindex.py`: stdout diff `0` vs CPython.
- `tools/run_bughunt.py`: **36/36 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION`; `int_loop +1.2%`, `list_comp -0.2%`,
  `dict_ops +3.9%`.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Notes

- `tuple.index` already supported start/stop; its non-int start/stop error text
  still says "or None, not 'X'" instead of CPython's "or have an __index__
  method" — a minor wording gap left for the builtin-method-error pass.
