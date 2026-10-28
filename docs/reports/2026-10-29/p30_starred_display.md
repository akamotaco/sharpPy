# p30 — starred unpacking in list/tuple/set display literals

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured divergence (before)

Starred elements inside **container display literals** were not unpacked; the
starred iterable was inserted as a single element:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `[*[2, 3]]` | `[2, 3]` | `[[2, 3]]` |
| `[1, *[2, 3], 4]` | `[1, 2, 3, 4]` | `[1, [2, 3], 4]` |
| `[*a, *a]` | `[2, 3, 2, 3]` | `[[2, 3], [2, 3]]` |
| `(*a,)` | `(2, 3)` | `([2, 3],)` |
| `{*a}` | `TypeError: unhashable type: 'list'` (stored the list) | wrong |

Starred **call** arguments (`max(*a)`) and starred **assignment targets**
(`x, *y = …`) already worked — only display literals regressed. This is core
Python 3 syntax, so it is treated as a priority regression.

## Root cause

`runtime/compile.cs`:
- `ListExpression` non-constant fallback did `foreach element: CompileExpression;
  BUILD_LIST count`. A `StarredExpression` compiles (its own case) to just
  `CompileExpression(starred.Value)`, pushing the iterable as one stack value,
  so `BUILD_LIST` counted it as a single element.
- `TupleExpression` and `SetExpression` fallbacks had the same shape.

## C vs C#/CLR translation

CPython `compiler_display` emits, for a literal with a starred element:
- list: `BUILD_LIST 0`, then per element `LIST_APPEND 1` (regular) or
  `<iterable>; LIST_EXTEND 1` (starred).
- tuple: same list build, then `LIST_TO_TUPLE` intrinsic (a tuple literal with a
  starred element has no fixed size, so it cannot use `BUILD_TUPLE n`).
- set: `BUILD_SET 0` with `SET_ADD 1` / `LIST_EXTEND 1` (CPython `listextend`
  extends a set too).

The only VM change needed was that `LIST_EXTEND` had hard-cast its target to
`PyList`; CPython's `listextend` accepts both list and set.

## Fix

- `runtime/compile.cs`:
  - `ListExpression`, `TupleExpression`, `SetExpression` now detect a starred
    element and, only in that case, build incrementally with
    `BUILD_{LIST,SET} 0` + `LIST_APPEND`/`SET_ADD`/`LIST_EXTEND`; tuples finish
    with the `INTRINSIC_LIST_TO_TUPLE` intrinsic. The non-starred fast paths
    (constant folding, `BUILD_LIST/BUILD_TUPLE/BUILD_SET n`) are untouched.
- `runtime/PyVM.cs` `LIST_EXTEND`: accept `PyList` **or** `PySet` targets
  (previously `(PyList)` hard cast), matching CPython `listextend`.

## Verification

- `tests/bughunt/p30_starred_display.py`: stdout diff `0` vs CPython (alone /
  lead / trail / mid / multiple / tuple / set with dedup / generator / str /
  range / empty / nested `[*[*a]]` / dict.values / custom `__iter__` / starred in
  call args / return / `**` dict merge / non-iterable error
  `Value after * must be an iterable, not int`).
- Expanded scratch matrix (`.artifacts/tmp/star.py`, `star2.py`): all cases
  match, including `[*5]` → `TypeError: Value after * must be an iterable, not
  int` and `f"{[*a]}"`.
- `tools/run_bughunt.py`: **37/37 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: `list_comp -1.1%`, no `REGRESSION` (non-starred display
  paths unchanged).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.
