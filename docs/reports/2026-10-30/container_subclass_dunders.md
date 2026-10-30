# Container subclasses: the declared dunder, the inherited slot, and one outright crash

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Follows `e70c3ea` and `054eb8c` (the numeric family). Registers P0-24 for what is left.

## Scope measured before touching anything

The numeric work proved one pathology: SharpPy implements a builtin subclass's operation on the
carried value, CPython resolves the slot through `type(v)`. Before editing, the same probe shape was
run across the container families (`.artifacts/tmp/container_sub.py`), so the fix list came from
measurement rather than from a guess about which classes matter.

Seven divergences came out of it. Four are fixed here, three plus one new finding are registered as
P0-24.

## Fixed

**`len()` of a plain dict subclass was broken, not merely wrong.**

```python
class DPlain(dict): pass
len(DPlain({"a": 1}))
```

CPython: `1`. SharpPy: `TypeError: object of type 'DPlain' has no len()`.

`PyClassInstance.Length()` consulted only `_strStorage`. The instance does carry `_dictStorage` and
`_listStorage` — created in the constructor by `IsDictSubclass()` / `IsListSubclass()` — but `Length`
never looked at them, and the base implementation only knows how to find a Python-level `__len__`.
So a dict subclass without its own `__len__` had no path at all. That also means `len()` worked on
`DUser` (which declares `__len__`) and hid the hole.

**Ordering: the declared dunder was losing to the storage.** The same methods consulted storage
*before* `base`, which in CPython's terms puts the inherited `mp_length` ahead of the class's own
`__len__`. Now: declared dunder first, then dict / list / str storage, then base. The same ordering
fix went into `Contains()`, where `_dictStorage.Contains(item)` ran before the class's
`__contains__`.

**`PyStrSubclass`**: `__add__`, `__mul__`, `__mod__`, `__len__`, `__contains__` and the rich-compare
family now ask the user frames first. Before: `SUser("abc") + "z"` was `'abcz'` and `su == "z"` was
`False`; CPython answers `'user-str-add'` and `'user-str-eq'`.

`x in s` coerces: CPython always yields a bool from `in`, so a `__contains__` returning a string
reads as `True`. The probe pins both the value and the result type, because a truthful-looking
implementation that returns the raw string would still print `True` for the obvious case and diverge
elsewhere.

`__len__` results go through the CPython conversion (`_PyNumber_Index`), so a `__len__` returning
`'short'` raises `TypeError: 'str' object cannot be interpreted as an integer` rather than
propagating a weird value or reporting the wrong message.

## What is left, with its measured shape (P0-24)

1. **str subclass `__iter__`.** Routing it to the user dunder produced a new failure:
   `TypeError: 'list_iterator' object is not callable`. The user method is called and does return a
   `list_iterator`; something downstream calls that iterator again. The identical code on a plain
   class (`class P: def __iter__`) works, so the extra call is specific to this path and is outside
   `PyStrSubclass`. I did **not** ship the routing: the payload iterator stays, which gives a wrong
   value (`['a','b']` instead of the user's `['u','s','e','r']`) instead of a crash, and the code
   says so where it stands. Chasing it needs the `iter(obj)` call chain traced, which is a separate
   increment.
2. **int subclass `__iter__`** — `PyIntSubclass` does not override `GetIterator()` at all, so
   `iter(Q(1))` raises `'Q' object is not iterable` where CPython returns a `list_iterator`.
3. **tuple subclass** — `len`, `__iter__`, `__add__` ignore the declared dunder.
4. **bytes subclass** — same shape for `len`, `__iter__`, `__add__`.

For 3 and 4 there is a structural reason worth recording: `PyTuple` and `PyBytes` have **zero**
references to `_customType`, the field `PyObject` uses to carry a Python-level subclass type. Making
them honour dunders puts a user-frame lookup on the hottest paths in the interpreter, so it needs an
early return on `_customType == null` and a real measurement, not a hopeful patch.

## Performance: the flag was noise, again

`tools/perf_probe.py` flagged `dict_ops +15.6%` and `closure_calls +21.5%` on one run, after
reporting `+4.6%` and `+3.9%` on the previous run of the same binary. Per the standing rule, that is
neither automatically real nor automatically noise, so the two binaries were interleaved: five
rounds, old (`054eb8c`) and new, in alternating order, same suite, symmetric directories
(`.artifacts/tmp/ab/old`, `.artifacts/tmp/ab/new`).

| workload | old median | new median | delta |
| --- | --- | --- | --- |
| int_loop | 0.2150 | 0.2138 | -0.6% |
| dict_ops | 0.0622 | 0.0621 | -0.2% |
| closure_calls | 0.0501 | 0.0489 | -2.4% |
| comp_lambda | 0.0346 | 0.0349 | +0.9% |
| list_comp | 0.0953 | 0.0985 | +3.4% |
| str_fmt | 0.0923 | 0.0929 | +0.7% |
| unpack_star | 0.2326 | 0.2320 | -0.3% |
| call_heavy_fib20 | 0.0205 | 0.0202 | -1.5% |
| **TOTAL** | **0.8026** | **0.8033** | **+0.1%** |

No regression. The individual round rows explain the flag: the same binary produced `list_comp`
between 0.0578 and 0.1026 across rounds, and the slowest `closure_calls` of the whole experiment
(0.0730) came from the *old* binary in round 0. This is the third time a repeated `perf_probe` flag
has failed interleaved verification, so the flag threshold is a screening device here, not a verdict.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p59_container_subclass.py` | 34 cases, CPython diff **0** |
| `tools/run_bughunt.py` | **64/64** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| interleaved A/B perf | no regression, TOTAL +0.1% |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |
