# p68 — the numeric attribute surface (P0-30)

Date: 2026-10-30 · branch `work/bug-hunt` · reference CPython 3.12.15 (commit-pinned clone)

The audit that found this item started from a simple question: what does CPython put in
`long_getset[]`, `float_getset[]`, `complex_getset[]` and the matching method tables? Reading those
tables turned three vague divergences into three specific rules.

## What CPython actually does

```text
Objects/longobject.c:6207-6247   long_methods / long_getset
    conjugate, real, imag, numerator, denominator, bit_length, bit_count, to_bytes, from_bytes,
    as_integer_ratio, __trunc__, __floor__, __ceil__, __round__, __getnewargs__, __format__, __sizeof__
Objects/longobject.c:5358-5367   long_long(self):
    if (PyLong_CheckExact(v)) return Py_NewRef(v);   else return _PyLong_Copy(v);
Objects/floatobject.c:1787-1800  float_getreal → PyFloat_FromDouble(ob_fval)   (always a new object)
Objects/floatobject.c:1714-1723  float.__getnewargs__  → (double,)
Objects/complexobject.c:653-664  complex.__getnewargs__ → (real, imag)
Objects/descrobject.c:255-264    descr_check / getset_set error texts
```

Three consequences, all of which SharpPy got wrong:

**1. The self check is `PyLong_Check`, and `bool` passes it.** `(True).real` is `1`. Because the CLR
does not model `bool` as a `PyInt`, every check site has to say so; `TryLongSelf` now does, so
`real`, `imag`, `numerator`, `denominator`, `conjugate` and `__getnewargs__` work on `bool`.

**2. The value returned is a copy in the non-exact case, not the receiver.** `long_long` keeps
identity only for an exact int; a `bool` or an `int` subclass becomes a plain int. `float_getreal`
always builds a new float. Measured: `type(I(3).real)` was `'I'` and is now `'int'`,
`type(F(2.5).real)` was `'F'` and is now `'float'`.

**3. Assignment consults the merged type dict.** `_PyObject_GenericSetAttrWithDict` resolves through
`_PyType_Lookup`, which does not care whether the name came from a user class or a native type.
SharpPy's setter scanned only `PyClass` frames, so `float`'s `real` getset was invisible to
assignment and `setattr(2.5, "real", 1)` answered `'float' object has no attribute 'real'` where
CPython reports the getset's own `attribute 'real' of 'float' objects is not writable`. That one line
of the setter is why the delete path and the set path disagreed about the same attribute.

## The error-text family this pulled out

CPython's method descriptors prefix the defining type; its slot wrappers use a different form
altogether. Both were missing:

| call | CPython | before |
|---|---|---|
| `(5).bit_length(1)` | `int.bit_length() takes no arguments (1 given)` | `bit_length() takes no arguments (1 given)` |
| `(5).conjugate(1)` | `int.conjugate() takes no arguments (1 given)` | same wrong form |
| `(2.5).is_integer(1)` | `float.is_integer() takes no arguments (1 given)` | same wrong form |
| `float.fromhex("0x1p3", 2)` | `float.fromhex() takes exactly one argument (2 given)` | same wrong form |
| `(2.5).__repr__(1)` | `expected 0 arguments, got 1` (slot wrapper form) | `__repr__() takes no arguments (1 given)` |
| `int.conjugate("a")` | `descriptor 'conjugate' for 'int' objects doesn't apply to a 'str' object` | a per-function invented variant |

The numeric files are pinned now. The same qualified form was already seen for
`bytearray.upper() takes no arguments (1 given)`, so it is recorded as its own sub-family of P0-29:
the fix belongs in the descriptor layer once, not in each hand-written string.

## Two things deliberately left open

- `int.__dict__["real"].__get__(5)` → CPython returns `5`; SharpPy's builtin descriptor objects expose
  no `__get__`/`__set__`/`__delete__` at all. Registered, not pinned in the probe: a probe that
  documents a missing capability freezes the wrong behaviour in place.
- `type(int.__dict__["real"]).__name__` → `getset_descriptor` in CPython, `property` here. Same family
  as the remaining P0-28 surface gaps.

## Gates

* `tests/bughunt/p68_numeric_attribute_surface.py` — 46 cases, CPython vs SharpPy diff **0**.
* `tools/run_bughunt.py`: **73/73**. `tools/evaluate.py`: unchanged `225 / 3 / 2`.
* Godot 4.7.2 mono: Release build 0 errors, `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`.
* `tools/perf_probe.py` flagged `str_fmt +24.6%` / `list_comp +24.1%`. Against `57419fc` the
  nine-round interleaved A/B gave `str_fmt −0.5%`, `TOTAL +1.2%`, and `list_comp +57.5%` — so the
  workload was measured in isolation, alternating binaries 11 times each:
  `old 0.1123…0.1168`, `new 0.1125…0.1167`, median **+0.4%**, min **+0.2%**. The distributions are
  identical; the flag was process noise, and the whole suite was sitting in the slow mode of a
  documented bimodal distribution. Neither workload performs attribute assignment at all, which is the
  only path this commit touches besides the three numeric type dicts.
