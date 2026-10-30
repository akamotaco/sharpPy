# P0-21: object_new / object_init argument rules — a message item that was really an execution hole

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Durable probe: `tests/bughunt/p61_object_new_init_rules.py` (37 cases, CPython diff 0).

## The ledger entry was wrong about its own severity

P0-21 was registered as "`__new__` keyword errors use CPython's type-call labels", with the note that
"the plain `A(x=1)` case already matches". Measured again at the start of this increment:

```python
class A: pass
A(x=1)
```

CPython: `TypeError: A() takes no arguments`.
SharpPy: **an instance**, no error.

So the common case was not matching; the arguments were being dropped on the floor and the object was
constructed anyway. Silently accepting constructor arguments is an execution defect — a class that
requires arguments can be built uninitialised — not a wording problem. The item got reclassified and
done properly.

## The rule, read out of the pinned source

`Objects/typeobject.c:5416-5457` reduces this to two predicates:

```c
excess_args(args, kwds)  ==  args non-empty || kwds non-empty

object_new(type, args, kwds):
    if (excess_args) {
        if (type->tp_new != object_new)  -> "object.__new__() takes exactly one argument (the type to instantiate)"
        if (type->tp_init == object_init) -> "%.200s() takes no arguments"      (tp_name)
        /* otherwise fall through: a user __init__ will report its own arity */
    }

object_init(self, args, kwds):
    if (excess_args) {
        if (type->tp_init != object_init) -> "object.__init__() takes exactly one argument (the instance to initialize)"
        if (type->tp_new == object_new)   -> "%.200s.__init__() takes exactly one argument (the instance to initialize)"
    }
```

Translated to SharpPy: *is the MRO resolution for `__new__`/`__init__` object's own descriptor, or a
user function / a builtin base's implementation?* Two helpers, `TypeNewIsObjectNew` /
`TypeInitIsObjectInit`, do the identity comparison against `object.__new__` / `object.__init__`, and
three call sites use them: `PyClass.CreateInstance` (type calls), `object.__new__` and
`object.__init__`.

That structure is what makes the cases fall out instead of being special-cased one by one:

| call | CPython | why |
| --- | --- | --- |
| `A(1)` | `A() takes no arguments` | new is object's, init is object's |
| `C(x=1)` with `__init__(self, x)` | `C.__init__() missing 1 required positional argument: 'x'` | object_new falls through, user `__init__` reports |
| `D(1)` with `__new__(cls)` | `D.__new__() takes 1 positional argument but 2 were given` | real slot receives the arguments |
| `object.__new__(A, 1)` | `A() takes no arguments` | same predicate, reached directly |
| `object.__init__(A(), 1)` | `A.__init__() takes exactly one argument (the instance to initialize)` | init is object's, new is object's |
| `object.__init__(C(1), 2)` | `object.__init__() takes exactly one argument (the instance to initialize)` | init is a user function |
| `MD(a=1)` where `MD(dict)` | builds the dict | dict's own `tp_new`, object_new never reached |

The last row is the one a naive "reject excess arguments in CreateInstance" patch would have broken,
so it is pinned in the probe along with `list`/`int` subclass construction.

## What is left, and why it is separate

Two builtin-specific argument grammars remain, both measured:

- `tuple.__new__(tuple, iterable=[])` → CPython `tuple() takes no keyword arguments`, SharpPy `()`.
- `H("ab", encoding="utf8")` for `class H(str)` → CPython `decoding str is not supported`.
- `str.__new__(H, 5, 6)` → CPython `str() argument 'encoding' must be str, not int`.

These are each builtin's own `PyArg_Parse*` signature (`tuple_new`, `unicode_new`), not the general
type-call rule, and they need translating per type rather than centrally.

One display gap also surfaced while comparing, and is registered rather than masked: object repr
addresses have different shapes (CPython `0x000002730A8DE3C0`, SharpPy `0x1189baa`). The probe
normalises instances to `<instance>` so this does not hide or fake a pass on the argument rules.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p61_object_new_init_rules.py` | 37 cases, diff **0** |
| `tools/run_bughunt.py` | **66/66** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| `tools/perf_probe.py` | no flag (the new check is two reference comparisons, reached only when arguments were actually passed to a constructor) |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |
