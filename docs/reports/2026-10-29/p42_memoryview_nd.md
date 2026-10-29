# p42 — `memoryview.cast` grammar, argument protocol and real N-D views

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-6, P0-7 (ledger `docs/known_gaps.md`)

## Measured divergence

Two clusters in one function, both re-measured against CPython 3.12.15 before touching code:

| call | CPython | SharpPy before |
|---|---|---|
| `m.cast("=I")` / `("<I")` / `(">I")` / `("!I")` | `ValueError: memoryview: destination format must be a native single character format prefixed with an optional '@'` | accepted, returned elements (so big-endian views silently gave wrong values) |
| `m.cast(b"I")` / `(73)` / `(None)` | `TypeError: cast() argument 'format' must be str, not bytes/int/None` | `ValueError: … native single character format …` |
| `m.cast(format="I")` | works | `TypeError: cast() takes no keyword arguments` |
| `m.cast()` | `cast() missing required argument 'format' (pos 1)` | `cast() takes from 1 to 2 positional arguments but 0 were given` |
| `m.cast("I", (2,), 1)` | `cast() takes at most 2 arguments (3 given)` | `…takes from 1 to 2 positional arguments but 3 were given` |
| `m.cast("I", format="B")` | `argument for cast() given by name ('format') and position (1)` | keyword rejected |
| `m.cast("I", (2.0,))` | `memoryview.cast(): elements of shape must be integers` | `memoryview: shape elements must be integers` |
| `m.cast("I", (0,))` | `ValueError: memoryview.cast(): elements of shape must be integers > 0` | accepted |
| `m.cast("I", "22")` | `shape must be a list or a tuple` | `memoryview: shape must be a tuple of integers` |
| `b16.cast("I", (2, 2))` | 2-D view, `tolist() == [[1, 2], [3, 4]]` | `TypeError: memoryview: only 1-dimensional cast is supported` |
| `v2.ndim / shape / strides / len` | `2 / (2, 2) / (8, 4) / 2` | — |
| `v2[0, 1]`, `v2[-1, -1]` | `2`, `4` | — |
| `v2[0, 5]` | `IndexError: index out of bounds on dimension 2` | — |
| `v2[0, 0, 0]` | `TypeError: cannot index 2-dimension view with 3-element tuple` | — |
| `v2[0]` / `v2[(0,)]` / `v2[()]` | `NotImplementedError: multi-dimensional sub-views are not implemented` / `sub-views are not implemented` / same | — |
| `v2[0:1]` | `[[1, 2]]`, ndim still 2 | — |
| `mv.c_contiguous / contiguous / readonly / suboffsets / obj` | present | `AttributeError` |
| `hasattr(mv, '__getitem__' / '__setitem__' / '__len__')` | True | False (`__setitem__` unreachable even via `mv[0] = x` written form) |

Note that CPython itself refuses sub-views for `ndim > 1`; the two refusals have
*different wording* (int index vs short tuple), so both texts are copied verbatim rather
than invented.

## Implementation (`core/PyBuffer.cs`, `type/PyBytes.cs`)

- **Format grammar** now matches `memory_cast`: one struct character, optional `@` only.
  The message is a shared `FormatError` constant; non-`str` gets the `k`-parser text, with
  CPython's quirk that the `None` type is spelled `None` while everything else uses its
  `tp_name`.
- **Keywords** `format` / `shape`, duplicate-binding and arity messages copied from the C
  argument parser.
- **Shape validation** in CPython's order: element type → element > 0 → container type →
  `product(shape) * itemsize != buffer size`. Lists accepted as well as tuples; bools count
  as integers (`PyNumber_Index` semantics via `PyObject.TryGetIndex`).
- **N-D model**: the flat storage view (`_offset`/`_length`/`_stride`) keeps working and
  gains `_shape` + row-major `_estr`. A one-element shape collapses back to the 1-D fast
  path. `tolist()` nests one list per remaining dimension; `Length()` reports axis 0 while
  `_length` stays the element count for storage arithmetic; `strides` multiplies element
  strides by the byte stride; dim-0 slicing rewrites axis 0 and axis-0 element stride
  (so `v2[::2]` reports `strides (8, 4)` → step-adjusted and keeps rank 2).
- **Indexing**: full-rank tuples fold to a flat element with per-axis bounds →
  `index out of bounds on dimension N`; over-long tuples and short tuples produce CPython's
  two distinct refusals; writable N-D element assignment folds through the same path.
- **Buffer attributes** `c_contiguous`, `contiguous`, `readonly`, `suboffsets`, `obj` were
  missing; `obj` needed an `_exportee` reference threaded through the constructors because
  `memoryview(raw).obj is raw` is an observable identity (the `bytes` buffer path only had
  the CLR `byte[]`).
- Materialized `__getitem__`, `__setitem__`, `__len__` as **slot wrappers** (matching
  CPython's wrapper-descriptor kind, including the `METH_VARARGS "OO"` leading space in
  `" expected 2 arguments, got 1"`).

## Verification

- `tests/bughunt/p42_memoryview_nd.py`: 111 lines, stdout diff **0** (format grammar,
  keyword/arity/duplicate messages, shape validation, 1-D/2-D/3-D views, indexing and all
  refusals, slicing, buffer attributes, unbound slot wrappers, writable N-D assignment).
- Supporting matrices `.artifacts/tmp/mvcast.py`, `mvnd.py`, `mvidx.py`, `mvattr.py`:
  24 → 5 → 0 divergences across the three stages (the residual 5 are the deferred items).
- `tools/run_bughunt.py`: **47/47 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no workload over the 15 % flag (max `closure_calls +11.3 %`,
  the known-noisy workload; memoryview is not on any measured path).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Explicitly deferred (registered)

- **P0-15**: `hash()` of a non-`B`/`b`/`c` view must raise
  `ValueError: memoryview: hashing is restricted to formats 'B', 'b' or 'c'`; needs a
  Python-level hash hook because a CLR `GetHashCode` override may not throw.
- **P0-4**: `memoryview.__eq__` (`memoryview(b"abc") == b"abc"` still `False`).
- **P1-8**: `__eq__` / `__ne__` / `__hash__` / `__buffer__` are still not visible through
  `hasattr` on `memoryview`; materializing comparison descriptors has broken CLR dispatch
  before (p22), so it needs the dispatch bridge rather than a per-type patch.
