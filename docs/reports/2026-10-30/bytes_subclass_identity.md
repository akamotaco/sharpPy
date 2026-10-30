# P0-27: `class B(bytes)` — identity, `bytes.__new__`, and the missing sequence slots

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Durable probe: `tests/bughunt/p63_bytes_subclass.py` (58 cases, CPython diff 0).

## What was measured

`class B(bytes): pass` produced an object that behaved like bytes but was not a B:

```python
type(B(b"xy")).__name__      # CPython 'B'      SharpPy 'bytes'
len(BUser(b"abc"))           # CPython 99       SharpPy 3        (__len__ ignored)
list(BUser(b"abc"))          # CPython [1,2,3]  SharpPy [97,98,99]
repr(BUser(b"a"))            # CPython 'user-repr'  SharpPy "b'a'"
3 * BUser(b"a")              # CPython b'aaa'   SharpPy TypeError
bytes.__new__(B, b"zz")      # CPython B        SharpPy TypeError: type.__new__() takes exactly 4 arguments
```

Twelve measured divergences, from three independent causes.

## Cause 1 — the construction path erased the subclass

`PyClass.Call` short-circuited on the first bytes base:

```csharp
foreach (var t in MRO)
    if (ReferenceEquals(t, PyType.BytesType))
        return PyBuiltinFunction.CallBytes(args, kwargs);   // plain bytes, cls ignored
```

CPython's `bytes_new` honours the type it was asked for: it builds the value under `bytes_new`'s own
argument rules and then allocates it as the subclass (`bytes_subtype_new`). That is what
`PyBytesSubclass.BytesSubtypeNew` now does, and `PyBytesSubclass : PyBytes` carries the byte payload
plus the subclass identity — the same shape as `PyTupleSubclass`, with attribute handling factored
into `SubclassInstanceSupport` (shared with the tuple version instead of copied).

A user-declared `__new__` still wins, because in CPython it is a real slot.

## Cause 2 — `bytes.__new__` did not exist

Attribute lookup fell through to the generic fallback and landed on `type.__new__`, so the ordinary
way of writing an immutable byte wrapper

```python
class C(bytes):
    def __new__(cls, v):
        return bytes.__new__(cls, v)
```

died with `type.__new__() takes exactly 4 arguments`. Registered on `bytes.__dict__` as it is in
CPython, honouring `cls`, with the measured foreign-class message
`bytes.__new__(X): X is not a type object (int)`.

## Cause 3 — no sequence slot wrappers on bytes

CPython's `bytes.__dict__` has `__add__`, `__mul__`, `__rmul__` and **no `__radd__`** (`sq_concat`
handles both sides in C). None were registered, so the reflected half was unreachable and
`3 * B(b"a")` raised `unsupported operand type(s) for *`. They are `PyWrapperDescriptor`s now, which
also gives CPython's wrapper arity/keyword messages:

- `b"a".__mul__(b"b", x=1)` → `wrapper __mul__() takes no keyword arguments`
- `b"a".__add__(b"b", b"c")` → `expected 1 argument, got 2`

## The trap worth remembering: `LookupUserSpecial` on a class object

The gate for "did the user declare `__new__`?" was first written with `LookupUserSpecial("__new__")`.
It returned non-null for `class B(bytes)` - because that helper walks `GetPyType().MRO`, and for a
class object `GetPyType()` is the **metaclass**. So it answered "does `type` declare `__new__`".
Confirmed with temporary instrumentation in a worktree (never in the working tree):

```
DBG CI B derives=True userNew=PyStaticBuiltinMethod getPyType=type newMethod=PyNewMethodDescriptor mro=B,bytes,object
```

`PyClass.DeclaresUserSpecial(name)` is the class-object question: walk *this* class's MRO, consult
only `PyClass` frames, stop at the first builtin frame. The instance question and the class question
are different, and the code now says so.

## Remaining duplication, stated rather than hidden

There are two constructor entry points: `PyType.CreateInstance` (`virtual`) and
`PyClass.CreateInstance`, declared `public new` - so shadowed, and a virtual call through a `PyType`
reference still runs the **base** implementation. `PyType.Call` therefore takes one path while
`PyTypeMetaclass.cs:211` (`pyClass.CreateInstance(args)`) takes the other. bytes is now gated on both
sides, which is correct but is two places where CPython has one. Collapsing them is a follow-up.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p63_bytes_subclass.py` | 58 cases, diff **0** |
| `tools/run_bughunt.py` | **68/68** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| interleaved A/B vs `2ef7b82`, 9 rounds | `str_fmt +3.5%`, `list_comp +1.9%`, **TOTAL +1.1%** (inside the noise band already established for these two workloads) |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |
