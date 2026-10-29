# p44 — numeric coercion dunders: `PyNumber_Long` / `PyNumber_Float` slot order

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-10 (and the `float()` signature/text items found alongside it)

## C source first (`cpython-v3.12.15`, commit-pinned)

`int(x)` with no base is `long_new_impl` → **`PyNumber_Long(x)`** (`Objects/longobject.c:5598`),
and `float(x)` is **`PyNumber_Float(x)`** (`Objects/abstract.c`). Reading the two functions
settled a question the ledger had backwards:

```
PyNumber_Long:  PyLong_CheckExact → m->nb_int → m->nb_index → __trunc__ (deprecated)
                → str → bytes → bytearray → buffer → TypeError
PyNumber_Float: PyFloat_CheckExact → m->nb_float → m->nb_index → float subclass
                → PyFloat_FromString
```

* `int()` consults **`__int__` first**, then `__index__` — the ledger said the opposite.
* `float()` consults `__float__`, then `__index__`, and **never** `__int__`.
* `nb_int` results must satisfy `PyLong_Check`, so `bool` and int subclasses are accepted
  (CPython only *warns* for strict subclasses); `nb_float` must satisfy `PyFloat_Check`, and
  its error message is prefixed with the **object's** type name.

## Measured divergence (22 items before the fix)

| call | CPython | SharpPy before |
|---|---|---|
| `int(WithInt())` / `int(WithIndex())` | `7` / `3` | `TypeError: int() argument must be a string, a bytes-like object or a real number, not 'WithInt'` |
| `int(Both())` (`__int__` 70, `__index__` 30) | `70` | `TypeError` |
| `float(Both())` | `30.0` (index wins; `__int__` ignored) | `TypeError` |
| `float(WithIndex())` | `3.0` | `TypeError` |
| `int(IndexBig())` (`__index__` → 2**70) | `1180591620717411303424`, exact `int` | `TypeError` |
| `int(IntReturnsFloat())` | `TypeError: __int__ returned non-int (type float)` | generic `int() argument …` |
| `int(IndexReturnsStr())` | `TypeError: __index__ returned non-int (type str)` | `TypeError` |
| `int(IntReturnsBool())` | `1` | — |
| `int(Raises())` / `float(Raises())` | user exception propagates | replaced by `TypeError` |
| `float(FloatReturnsInt())` | `FloatReturnsInt.__float__ returned non-float (type int)` | generic message |
| `float(b"2.25")`, `float(bytearray(b"1.25"))`, `float(memoryview(b"1.5"))` | values | `TypeError: float() argument must be …` |
| `float(x=1)` | `TypeError: float() takes no keyword arguments` | returned **`0.0`** |
| `float("1", 2)` | `float expected at most 1 argument, got 2` | `… at most 1 argument (2 given)` |
| `float("1_0.5")` / `float("1e1_0")` | `10.5` / `1e10` | `ValueError: could not convert string to float` |
| `float("_1")`, `float("1__0")`, `float("1_")`, `float("1._5")` | `ValueError` | CLR parser accepted some of these |
| `float(b"x")` | `could not convert string to float: b'x'` (reports the object) | would report decoded text |
| `float(BigIndex())` where `__index__` → 10\*\*400 | `OverflowError: int too large to convert to float` | returned `inf` |

## C → C# translation

CPython fills `nb_int`/`nb_float`/`nb_index` slots **only when the class defines the
dunder**; native types carry real C functions. SharpPy has the same split, differently: the
builtin conversions are CLR virtuals (`PyInt.AsFloat`, `PyString.ToFloat`, …) and user
classes have nothing but class-dict functions. So the dunder half belongs on
`PyClassInstance`, and the native half must keep using the fast virtuals:

- `type/PyClass.cs`: `PyClassInstance.AsInt()` = `__int__` → `__index__` → CPython message;
  `AsFloat()` = `__float__` → `__index__` → CPython message. `AsInt`/`AsFloat` are the
  `As~` (Python-type, unbounded) side of the convention, so the arbitrary-precision rule
  survives; `__index__` results go through `PyInt.AsFloat()` rather than a `(double)` cast,
  which is what keeps `int too large to convert to float` raising instead of yielding `inf`.
  `bool` is accepted as an `__int__` result because `PyLong_Check` does.
- `core/PyType.cs`: `CreateInstance` now intercepts `FloatType` (the global `float` **is**
  `PyType.FloatType`, so `PyBuiltin.CallFloat` was a dead second implementation for this
  path) and routes to `args[0].AsFloat()` with CPython's keyword/arity rules.
- `type/PyFloat.cs`: added `TryParsePythonText` (trim, PEP 515 underscores only between
  digits, `inf`/`infinity`/`nan` with sign, `NumberStyles.Float` + `InvariantCulture`) and
  `FromBufferBytes`, whose failure message reports `source.ToRepr()` — CPython says
  `b'x'` / `bytearray(b'x')`, not the decoded characters.
- `type/PyBytes.cs`, `type/PyByteArray.cs`, `core/PyBuffer.cs`: `AsFloat()` overrides for the
  buffer text forms; `type/PyString.cs`: `ToFloat` tail now shares the same parser instead of
  the locale-dependent `double.TryParse`.

## Verification

- `tests/bughunt/p44_numeric_coercion.py`: 91 lines, stdout diff **0** (slot order on one
  object carrying all three dunders, validation texts, exception propagation, arbitrary
  precision through `__index__`, buffer text forms, keyword/arity, underscore rules).
- Supporting matrices `.artifacts/tmp/num10.py`, `num10b.py`, `fnum.py`: 22 → 0 divergences.
- `tools/run_bughunt.py`: **49/49 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: one run flagged `str_fmt +19.5 %` / `list_comp +73.4 %`; three
  same-binary re-runs gave `str_fmt +2.4 / +5.4 / +11.6 %` and `list_comp −0.6 / −1.1 /
  −1.7 %` → machine noise, and `list_comp` never moved once the spike was gone.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Found and registered, not fixed here

- **P0-16**: `class FSub(float)` instances carry no float value at all —
  `repr(FSub(2.5))` → `<__main__.FSub object at 0x…>`, `FSub(2.5) + 1` → `TypeError`,
  `FSub(2.5) == 2.5` → `False`. `int`/`str` subclasses work because `PyIntSubclass` /
  `PyStrSubclass` exist; there is no `PyFloatSubclass`. Excluded from the durable probe.
- Deprecated paths left unimplemented on purpose: `int()` → `__trunc__` delegation (CPython
  raises a `DeprecationWarning` first) and strict-subclass returns from `__int__`/`__float__`.
- `float(memoryview(b"x"))`'s failure text embeds the CLR pointer width
  (`<memory at 0x1024E52>` vs CPython's 64-bit form); address text is not parity-tested.
