# A bound method equals itself (P0-54, L2.2 세 번째 커밋)

## Before, measured

`.artifacts/tmp/l22_method_eq.py` runs one program on both interpreters and diffs every line.
Before the change SharpPy lost six observations, all of them from the same missing slot:

| observation | CPython | SharpPy (before) |
|---|---|---|
| `o.f == o.f` | True | **False** |
| `o.f != o.f` | False | **True** |
| `o.c == o.c` (classmethod) | True | **False** |
| `o.c == O.c` | True | **False** |
| `hash(o.f) == hash(o.f)` | True | **False** |
| `{o.f: 1}[o.f]` | `1` | **`KeyError: <bound method O.f of ...>`** |

`PyMethod` (core/PyFunction.cs:626) had `Instance` and `Function` but no rich-compare and no hash
override, so every comparison fell through to CLR reference equality — and two attribute lookups
of the same method are two different objects.

## What CPython does

`method_richcompare` (`Objects/classobject.c:245`):

- only `==` / `!=`, and only against another bound method, otherwise `NotImplemented` (`:251`-`:256`)
- `__func__` compared with `==` (`:259`)
- `__self__` compared by **pointer identity** (`:261`)

`method_hash` (`:299`) mixes the receiver's address hash (`:303`) with the function's own hash
(`:304`) by xor (`:307`), and maps `-1` to `-2` (`:308`).

The identity half is what makes the following true, and it is the reason an instance's own `__eq__`
must not be consulted:

```python
class Weird(O):
    def __eq__(self, other): return True
w = Weird()
w.f == w.f        # True  - same receiver object, not because __eq__ says so
o.f == Weird().f  # False - different receiver, __eq__ never runs
```

`o.s == o.s` (staticmethod) is True for a different reason: a staticmethod descriptor hands out the
same function object, so it is identity from the start.

## The same rule was missing on the builtin side, too

While pinning the fix, `.artifacts/tmp/l22_builtin_m2.py` measured the sibling type - a builtin
method bound to a receiver - and SharpPy lost it in exactly the same way:

| observation | CPython | SharpPy (before) |
|---|---|---|
| `l.append == l.append` | True | **False** |
| `(1).bit_length == (1).bit_length` | True | **False** |
| `hash(l.append) == hash(l.append)` | True | **False** |
| `{l.append: 1}[l.append]` | `1` | **`KeyError: <built-in method append of list object at ...>`** |
| `l.append in {l.append}` | True | **False** |
| `l.append == l.extend` | False | False |
| `{}.keys == {}.keys` | False | False |

CPython's rule for that type is `meth_richcompare` (`Objects/methodobject.c:292`): `m_self` by
identity (`:306`), then the wrapped C function pointer (`:308`), `==`/`!=` only (`:298`-`:303`), and
`meth_hash` (`:316`) xors the function part with the receiver's hash. Leaving this half unfixed
would have meant one operation with two meanings - `o.f == o.f` true but `l.append == l.append`
false - so it is in this commit rather than a later one.

## The change

`core/PyFunction.cs` — two overrides on `PyMethod`:

- `RichCompare` reproduces `method_richcompare` including the `NotImplemented` return, so
  `o.f == 5` still falls through to `False` and `o.f == O.f` stays `False`
  (a plain function is not a bound method).
- `ToHash` uses `PyObjectId.Of(Instance)` for the receiver part — this interpreter's stable identity
  number, the same one `id()` uses since P0-47 — xored with `Function.ToHash()`.

The builtin half is the same pair of slots on the two CLR classes that stand for a bound
`PyCFunctionObject`: `PyBuiltinBoundMethod` (core/PyFunction.cs:725, made at type/PyClass.cs:2143)
and `PyBoundMethodDescriptor` (core/PyDescriptor.cs:160). Two classes is already one too many -
they are one CPython type - so both carry the same slot with a cross-reference instead of a second
invention. `BuiltinFunction` / `MethodDescriptor` reference identity plays the role of `ml_meth`.

User classmethods bind to `PyMethod` with the class as receiver, which is why `o.c == o.c` and
`o.c == O.c` are fixed by the same two methods rather than by a second special case. Builtin
methods (`PyBoundMethodDescriptor`, `PyBoundClassMethod`) keep their own reference equality, which
matches CPython: `l.append == l.append` is False there too.

## The L2 leftover, confirmed

`atexit.unregister` compares with `PyObject.Compare` (L2.1), so it needed this rule to work at all
for bound methods. Measured with a callback that prints:

```python
atexit.register(o.m)
atexit.unregister(o.m)   # before: the callback still ran and printed 'ran'
```

Both interpreters now print only `done` (`.artifacts/tmp/l22_unreg.py`). `tests/bughunt/p90_bound_method.py`
pins the same property through `atexit._clear()` + `_ncallbacks()`.

## Performance

The added code runs only when a method is compared or hashed; no `bench_suite.py` workload does
either. `.artifacts/tmp/p54_ab.py` alternates base and new binaries over `bench_suite.py`, 5 rounds, median:

```
closure_calls    1.11     str_fmt  1.08     others 0.99 - 1.05
```

Neither workload touches the change (`closure_calls` is pure closures — no bound method is ever
compared or hashed), so the same script was run against the same binary twice as a control:

```
closure_calls    0.88     str_fmt  0.92     list_comp 1.06
```

The swing is the same size in the opposite direction with an identical binary, which makes this the
known suite noise (already recorded for `str_fmt` / `list_comp`), not a cost of P0-54.

## Gates

| gate | value |
|---|---|
| build | 0 errors |
| `tests/bughunt/p90_bound_method.py` | matching (Python methods, builtin methods, hashing, atexit) |
| `tools/run_bughunt.py` | **98/98 matching** |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot replay | `82 matched, 0 other`, `REPLAY_THREADS 2 main=2` |

## Residual

- `hash(o.f)` values are not CPython's values (identity numbers, not addresses); only the equality
  and consistency properties are pinned.
- `-1 → -2` from `method_hash:308` is not reproduced because SharpPy hashes are CLR `int` and do
  not use `-1` as the error sentinel.
- `PyBoundClassMethod` (core/PyBuiltinClassMethod.cs:81) is a private builtin-side type that still
  compares by reference. Nothing measures it today; it is the third CLR class standing in for one
  CPython callable type, which is P0-46-style duplicate debt.
- `type(math.sqrt).__name__` is `'object'` in SharpPy where CPython says `'builtin_function_or_method'`
  (registered as **P0-66**). The comparison itself already matches; only the type name is wrong.
