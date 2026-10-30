# tuple subclasses, and a wrong guess about `_customType`

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Continues P0-24; closes item 3 and re-scopes item 4.

## What the ledger said, and what was actually there

P0-24 recorded tuple and bytes subclasses as "dunder dispatch missing, and `PyTuple`/`PyBytes` have
zero `_customType` references". That last observation was true and the inference drawn from it was
wrong: I assumed subclass instances carried their Python type in `_customType`, and wrote

```csharp
if (HasCustomType) { ...consult the dunder... }
```

Built, measured, **nothing changed**. Tuple subclass instances are `PyTupleSubclass : PyTuple`
(living in `type/PyClass.cs`), which tracks its type through an `InstanceType` field and an
`GetPyType()` override — `_customType` stays null, so the guard rejected exactly the cases it was
meant to handle. `t_add` was the accidental tell: it went through a helper *without* the guard, and
was the one case that started working.

Removing the guard is correct rather than a workaround because `LookupUserSpecial` already stops at
the first non-`PyClass` MRO frame. An exact tuple's MRO begins with `PyType.TupleType`, so the lookup
returns null after one `GetPyType()` and one type test — no dictionary scan, no delegate call.

## `list()` was not going through iteration

`t_len` fixed immediately; `t_iter` did not. CPython's `list(t)` is `PySequence_List`, which goes
through `tp_iter`. SharpPy's `PyTuple.AsList()` copied `Items` directly — right answer for a plain
tuple, and a total bypass of `__iter__` for a subclass. Now `AsList()` defers to `base.AsList()`
(which drives `GetIterator()`) whenever the class declares `__iter__`.

That is the second time in this family that a builtin's convenience path (`AsList`, `Length`,
payload arithmetic) answered *correctly for the base type* and thereby hid the fact that the
protocol was never consulted.

## The covariant return type problem

`PyTuple.GetIterator()` is declared `public override PyIterator GetIterator()`, narrower than the
base `PyObject` return. A user `__iter__` may return anything with `__next__` — including a
`PyClassInstance`, which is an iterator in CPython's sense (`tp_iternext` dispatches to `__next__`)
but is not a CLR `PyIterator`. Added `PyObjectIteratorShim`, which forwards `Next()` to the object's
`Next()`, so such an iterator can flow through a `PyIterator`-typed slot instead of throwing an
`InvalidCastException`.

## bytes: the subclass never existed

The bytes divergences were not a dispatch problem. Measured:

```python
class B(bytes): pass
type(B(b"xy")).__name__   # CPython: 'B'     SharpPy: 'bytes'
```

The subclass identity is dropped at construction, so no dunder can ever be reached and
`len`/`__iter__`/`__add__` divergences are all downstream of that one defect. There is no
`PyBytesSubclass` (there is a `PyTupleSubclass`). Fixing it means a wrapper *and* the construction
path, which is a separate increment, recorded as such with the reproducer
(`.artifacts/tmp/tbsub.py`) rather than patched halfway.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p59_container_subclass.py` | +18 tuple cases (len, iter, for, add, mul, index, count, slice, unpack, hash-as-dict-key), diff **0** |
| `tools/run_bughunt.py` | **64/64** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| `tools/perf_probe.py` | no flag |
| interleaved A/B vs `90b33a0`, 5 rounds | TOTAL **+0.3%**, no workload beyond noise |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |

The A/B mattered here even though the probe flagged nothing: `Length`, `Add`, `AsList` and
`GetIterator` on tuples are among the hottest methods in the interpreter, and an added lookup there
is exactly the kind of change a probe run is too coarse to catch.
