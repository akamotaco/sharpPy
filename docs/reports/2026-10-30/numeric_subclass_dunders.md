# P0-23 closure — a numeric subclass resolves its arithmetic through the MRO

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Closes P0-23, the five residuals left after the P0-18 increment 6 commit (`e70c3ea`).

## The shape of the remaining bug

`PyIntSubclass` and `PyFloatSubclass` carry a value. Every arithmetic operation on them was written
as a call into that value:

```csharp
public override PyObject Add(PyObject other) => _floatValue.Add(other);
```

CPython resolves the slot through the *subclass*: `binary_op1` reads `type(v)->nb_add`, and for a
heap type that slot is the wrapper around whatever `__add__` the class or its bases publish. So:

```python
class FUser(float):
    def __add__(self, other): return "user-add"
```

CPython answers `'user-add'`. SharpPy answered `4.5`. Same for `__radd__`, `__divmod__`,
`__floor__`, `__ceil__`, `__trunc__`, `__float__`, `__int__`, and for the int subclass. The value
carrying classes never consulted the type at all.

## One wrinkle that decides the design

The obvious lookup — `LookupSpecialMethod` over the MRO — **recurses**. `int.__divmod__` is now a
registered slot whose delegate calls `self.FloorDivide(...)`, and `FloorDivide` on the subclass is
the very virtual we are inside. The chain is: virtual -> slot -> virtual.

So the lookup is narrowed to the frames that can only be user code:

```csharp
internal PyObject LookupUserSpecial(string name)
{
    foreach (var mroType in GetPyType().MRO)
    {
        if (mroType is PyClass userClass && userClass.ClassDict.TryGetValue(name, out var declared))
            return declared;
        if (!(mroType is PyClass))
            break;   // the C slots take over from here
    }
    return null;
}
```

That is the same rule CPython applies, expressed in the CLR's terms: a user dunder wins, anything
else falls through to the inherited base slot — which is what the payload call is.

## The fixes

- **`PyObject.LookupUserSpecial` / `CallUserSpecialOrNull`** — the narrowed lookup.
- **`PyIntSubclass` / `PyFloatSubclass`** — every arithmetic virtual (`add`, `sub`, `mul`,
  `truediv`, `floordiv`, `mod`, `pow`, and `neg`/`pos`/`invert` where present) now asks the user
  frames first, then the payload.
- **`DivMod`** — not overridden on either wrapper at all, so `divmod()` never reached a dunder. Added
  to both, with the user dunder first.
- **`PyInt.DivMod`** — two bugs found by the probe: it required an exact CLR `PyInt`, so
  `divmod(2, IPlain(5))` raised `TypeError` where CPython answers `(0, 2)`; and it built the tuple
  from `FloorDivide(other)`/`Modulo(other)` on the *original* operand, which answer `NotImplemented`
  for a subclass, producing `(NotImplemented, NotImplemented)`. Both operands are now the resolved
  payloads, and a float operand on the right delegates to float arithmetic, as CPython's
  `long_divmod` does: `divmod(2, FSub(2.5))` is `(0.0, 2.0)`.
- **`PyFloat.DivModValues(left, right)`** — one implementation of `float_divmod`, shared by
  `__divmod__`, `__rdivmod__`, `PyFloat.DivMod`, `PyInt.DivMod`'s float case, and the wrappers. The
  reflected form is the same arithmetic with the operands the other way round; routing it through
  `other.DivMod(self)` could not work, because int's divmod answers `NotImplemented` for floats and
  there was nothing left to try.
- **`math.floor` / `math.ceil` / `math.trunc`** — tested exact CLR `PyFloat`/`PyInt` and threw
  `must be real number, not FSub` for every subclass. They now run the operand's `__floor__` /
  `__ceil__` / `__trunc__` slot, which is what `m_floor` and friends are in CPython. No recursion
  risk here: those slots read the payload directly rather than calling a virtual.
- **`int()` / `float()` on a float subclass** — `AsInt`/`AsFloat` on the wrapper returned the payload
  unconditionally. A user `__int__` / `__float__` now wins, with CPython's validation messages
  (`__int__ returned non-int (type X)`, `X.__float__ returned non-float (type Y)`).
- **`float.__int__` and `float.__float__`** published as slots. `__float__` returns the object
  itself, as CPython's does, so it needed the raw self rather than the payload.

## The asymmetry that had to be reproduced, not averaged

`operator.index(IUser(5))` where `__index__` returns 3 answers **5**, while `int(...)` and `float()`
honour the declared dunder. The probe pins both. index is special because for an int subclass, long's
C slot answers from the value without looking at the type at all; `__int__`/`__float__` genuinely go
through the slot wrapper. A single "special methods dispatch through the type" patch would have made
one of the two wrong.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p58_numeric_subclass_dunders.py` | 33 cases, CPython diff **0** |
| `tests/bughunt/p57_numeric_dispatch.py` | diff 0 |
| measurement probes (`subdunder`, `subops`, `subslots`, `numslot`, `fsub2`) | all diff **0** |
| `tools/run_bughunt.py` | **63/63** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| `tools/perf_probe.py` | no flagged workload |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |

## Still open

P0-18 and P0-23 are closed for the numeric family. Two things this work made visible and did not
claim to fix:

- `str` subclasses still lose their own `__len__`, `__iter__`, `__contains__` and `__reversed__` on
  the VM fast paths — the same class of bug, a different family. `tests/bughunt/p49_subclass_gates.py`
  records it as SKIP rather than pretending it passes.
- `PyWrapperDescriptor`-based slots have a fixed arity, so a wrapper that accepts more than CPython
  reports (`int.to_bytes(length=..., byteorder=...)`) is still P0-21's problem.
