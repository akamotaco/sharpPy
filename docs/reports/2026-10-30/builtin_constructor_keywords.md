# P0-25: builtin constructors were throwing keyword arguments away

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15.
Durable probe: `tests/bughunt/p62_constructor_keywords.py` (51 cases, CPython diff 0).

## How it was found

This was not on the plan. P0-21 was supposed to end with "two builtin `__new__` argument grammars
left", and while measuring those (`str.__new__(H, ...)`, `tuple.__new__(tuple, iterable=[])`) the
matrix produced 22 divergences instead of 2. Widening to the plain type calls (`typekw.py`) turned up
the real shape:

```python
dict(a=1)                  # CPython {'a': 1}    SharpPy {}
str(object=5)              # CPython '5'         SharpPy ''
complex(real=1, imag=2)    # CPython (1+2j)      SharpPy 0j
str(b"ab", "utf8")         # CPython 'ab'        SharpPy "b'ab'"
bool(x=1)                  # CPython TypeError   SharpPy False
```

Four of those are **silently wrong values**, not missing exceptions. `dict(a=1)` returning an empty
dict is the kind of bug a program carries for months.

The cause was one line at the end of `PyType.CreateInstance`:

```csharp
var builtinFunc = new PyBuiltinFunction(Name);
return builtinFunc.Call(args, null);   // kwargs dropped
```

Every builtin type not specially handled above that point — dict, list, tuple, str, complex, bool,
bytearray, … — lost its keyword arguments there.

## CPython's two refusals are different facts

Measured, not guessed:

| signature | refusal | types |
| --- | --- | --- |
| positional-only | `X() takes no keyword arguments` | `tuple`, `list`, `set`, `frozenset`, `bool`, `float`, `range`, `slice` |
| named parameters | `'x' is an invalid keyword argument for X()` | `str` (object/encoding/errors), `bytes`/`bytearray` (encoding/errors), `complex` (real/imag) |
| `**kwargs` is meaningful | accepts any keyword | `dict` |

Conflating them would create new bugs of its own, so the table is pinned in the probe with both
columns filled in.

## What was implemented

- **`PyType.CheckConstructorKeywords`** — the table above. On the hot path it costs one
  `kwargs == null` test; the `string[]` is only built when keywords were actually passed.
- **One `unicode_new` implementation, two entry points.** `PyStringNewArguments` holds CPython's
  `(object='', encoding='utf-8', errors='strict')` grammar, and `Project(args, kwargs, hasReceiver)`
  serves both `str.__new__` (where `args[0]` is the class) and the plain `str(...)` call. Str
  subclasses route through the same builder, so `H(b"ab", "utf8")` decodes.
- **`bytes`/`bytearray` text construction** (`PyBytesTextConstruct`), including the CPython refusal
  `string argument without an encoding` when a `str` arrives without a codec.
- **`dict` keywords become items**, positionally-overridden by the mapping argument's contents.
- **`tuple.__new__`** now has all three of `tuple_new`'s refusals: no keywords, at most one
  positional (`tuple expected at most 1 argument, got N`), and a foreign class
  (`tuple.__new__(H): H is not a subtype of tuple`). The last one also exposed that
  `cls.GetTypeName()` reports `type` for a class object — CPython reports the class's own `tp_name`.
- **Arity wording** fixed across the family: SharpPy said `X expected at most 1 arguments (2 given)`,
  CPython says `X expected at most 1 argument, got 2`.

## Two places where my own assumption was wrong

- I initially implemented `str(65)` as `chr(65)` → `'A'`. CPython answers `'65'`: `unicode_new` runs
  `PyObject_Str`, and `chr()` is a different function. Measurement caught it; the wrong draft is
  documented in the code comment so the reasoning isn't re-introduced.
- `System.Text.Encoding.GetEncoding("utf8")` fails on this runtime, so codec names are mapped
  explicitly (and unknown ones raise CPython's `LookupError: unknown encoding: X`). The alias-table
  gap, the UTF-8-only strict mode, and the unimplemented `surrogateescape`/`backslashreplace` handlers
  are recorded in `docs/known_gaps.md` rather than left implicit.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p62_constructor_keywords.py` | 51 cases, diff **0** |
| `tools/run_bughunt.py` | **67/67** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| `tools/perf_probe.py` | flagged `str_fmt +30.6%`, `list_comp +20.9%` — attributed as noise (below) |
| interleaved A/B vs `6a95c68`, 9 rounds | `str_fmt +1.7%`, `list_comp +3.0%`, **TOTAL -0.9%** |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |

The perf flags overlapped exactly with the code I touched, so they got a real A/B instead of a
hand-wave. Both binaries show a bimodal `list_comp` distribution (0.058–0.107 s) and the old binary
is sometimes the faster one; the added code is a null check on a path these workloads barely enter.

## Follow-ups registered

- **P0-26**: module-level builtin *functions* still reject keywords CPython accepts
  (measured: `enumerate(iterable=[1])`).
- **P1**: codec alias coverage, UTF-8-only strict decoding, abbreviated `UnicodeEncodeError` text.
