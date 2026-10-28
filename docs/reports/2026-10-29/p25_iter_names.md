# p25 — iterator type names + repr parity

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured divergence (before)

`type(it).__name__` and default `repr(it)` derived from `GetPyType().Name`.
Every built-in iterator returned the shared `PyType.IteratorType` (name
`iterator`), so SharpPy reported the generic name for all of them:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `type(iter([1,2]))` | `list_iterator` | `iterator` |
| `type(iter((1,2)))` | `tuple_iterator` | `iterator` |
| `type(iter("ab"))` | `str_ascii_iterator` | `iterator` |
| `type(iter("한글"))` | `str_iterator` | `iterator` |
| `type(iter({1,2}))` / frozenset | `set_iterator` | `iterator` |
| `type(iter({1:2}))` / `keys()` | `dict_keyiterator` | `iterator` |
| `type(iter({1:2}.values()))` | `dict_valueiterator` | `iterator` |
| `type(iter({1:2}.items()))` | `dict_itemiterator` | `iterator` |
| `type(iter(range(3)))` | `range_iterator` | `iterator` |
| `type(iter(b"ab"))` | `bytes_iterator` | `iterator` |
| `type(reversed([1,2]))` | `list_reverseiterator` | `reversed` |
| `type(reversed(range(3)))` | `range_iterator` | `reversed` |
| `type(reversed({1:2}))` | `dict_reversekeyiterator` | `reversed` |
| `type(reversed((1,2)))` / `reversed("ab")` | `reversed` | `reversed` |
| `type(enumerate([1]))` | `enumerate` | `iterator` |
| `repr(iter([1,2]))` | `<list_iterator object at 0x…>` | `<list_iterator object>` (no address) |

Subclasses had a `GetTypeName()` override and a `ToRepr()` override that
omitted ` at 0x…`, but `type(x).__name__` reads the *PyType*, which was shared,
so the name never surfaced.

## C vs C#/CLR reasoning

CPython declares a distinct `PyTypeObject` per iterator family
(`list_iter_type`, `tupleiter_iter`, `str_iter_type`,
`unicode_iter_type`, `set_iter_type`, `dict_keyiterator_type`, …,
`bytearray_iter`/`bytesiter`, `reversediter`/`range_iter`, `enum_enumerate`).
The name lives on the type, not the instance, so `type(it).__name__` and the
generic `repr` (`<name object at 0x…>`) come from the type object.

SharpPy modelled all iterators as CLR subclasses of one abstract `PyIterator`
that mapped to a single shared `PyType.IteratorType`. To reproduce the observable
name per family without hand-authoring a dozen `PyTypeObject`s, the fix mirrors
CPython by giving each family a cached, named `PyType` and routing name/repr
through it.

## Implementation

- `core/PyType.cs`: `GetIteratorType(string name)` — a cached per-name `PyType`
  (base `IteratorType`) so `type(it)` has the distinct `__name__`, stable
  identity, and still resolves the shared `__iter__`/`__next__` descriptors via
  MRO. `repr(type(it))` stays `<class 'NAME'>` (module defaults to `builtins`).
- `type/PyIterator.cs`: base gains `protected virtual string IteratorName`
  driving `GetTypeName()` and `GetPyType()`. Each family overrides
  `IteratorName`; the address-less `ToRepr()` overrides are removed so the base
  default `<{IteratorName} object at 0x…>` is used (now with the address).
  - list → `list_iterator` (constructor takes an optional type name, reused by
    bytes), tuple → `tuple_iterator`, str → `str_ascii_iterator`/`str_iterator`
    by ASCII-ness, range → `range_iterator`, set/frozenset → `set_iterator`,
    dict key/value/item → `dict_keyiterator`/`dict_valueiterator`/
    `dict_itemiterator`, empty → `empty_iterator`, enumerate → `enumerate`.
- `type/PyLazyIterators.cs`: `PyReversedIterator` takes a type name
  (default `reversed`) so reversed() reports per source.
- `type/PyBytes.cs`: `GetIterator()` → `bytes_iterator`.
- `core/PyBuiltin.cs` `CallReversed`: dict → `dict_reversekeyiterator`;
  list → `list_reverseiterator`; range → `range_iterator`; else `reversed`.
- `modules/AbcModule.cs` `_nativeCaps`: register every iterator family name
  (`__hash__`, `__iter__`, `__next__`) so `isinstance(it,
  collections.abc.Iterator/Iterable)` stays `True` despite the new distinct
  types.

## Verification

- New probe `tests/bughunt/p25_iter_names.py`: stdout diff `0` vs CPython
  (repr checked by `<NAME object at 0x` prefix to avoid volatile addresses).
- `tools/run_bughunt.py`: **32/32 matching**; `mismatches: []`.
  - Regression caught & fixed during the cluster: the first version derived new
    types from `object`, which dropped `it.__next__` attribute resolution
    (p03 `iter.sentinel`). Basing new types on `IteratorType` restores it.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no `REGRESSION`; `int_loop +0.9%`, `list_comp +0.0%`,
  `dict_ops -0.3%` (iteration hot paths flat — `GetPyType()` only runs on
  `type(x)`, not during iteration); `str_fmt/comp_lambda/closure_calls`
  +5–9% within the chronic noise band.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residual gaps recorded

- `type(it).__bases__` / `__mro__` show `iterator` as the base of each family
  (a SharpPy modelling choice to inherit the protocol descriptors); CPython's
  families derive directly from `object`. Not currently probed; revisit if a
  faithful MRO is required (would instead copy `__iter__`/`__next__`
  descriptors into each base-`object` type).
- `reversed(b"abc")` still raises `'bytes' object is not reversible`
  (CPython returns `[99, 98, 97]`); `bytes` lacks a `__reversed__` — a separate
  builtin-materialization gap, out of p25 scope.
