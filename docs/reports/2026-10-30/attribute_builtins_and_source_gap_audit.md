# Audit: duplicate and divergent implementations against CPython's source

Date: 2026-10-30 · branch `work/bug-hunt` · reference CPython 3.12.15 (commit-pinned clone) ·
SharpPy HEAD before this commit `119051f`

The question was structural: does SharpPy implement the same thing twice, does any of it disagree with
CPython's source, and is the overall C → C# translation faithful (allowing mechanism differences, not
output differences)? Two scanners were written (`.artifacts/tmp/dup_scan.py`, static;
`.artifacts/tmp/audit_runtime.py`, runtime class-path vs instance-path), plus the existing
`tools/bytecode_proximity.py`.

## 1. Two paths for one attribute lookup — the bug that made the rest visible

`PyObject.LookupAttribute` (the counterpart of `_PyObject_LookupAttr`) called
`GenericGetAttribute`. In CPython there is only one attribute path; here several types implement their
attributes in a `GetAttribute` **override** instead of `TypeDict` descriptors — `complex.real` among
them. Result: `(1+2j).real` worked while `hasattr(1 + 2j, "real")` answered `False`. Two answers about
one attribute.

Fix: `LookupAttribute` now runs the normal `GetAttribute` and suppresses only AttributeError, which is
exactly what CPython does (`Objects/object.c:1082-1147`), and it keeps the propagation rule a user
`__getattr__` depends on.

## 2. Duplicated implementations found

**Builtins with both a table entry and a module delegate** (the delegate wins, the table entry is dead
code that will drift): `abs, ascii, bin, compile, format, hex, isinstance, issubclass, len,
memoryview, oct, pow, vars` — 13 names, `core/PyBuiltin.cs` vs `modules/BuiltinsModule.cs` /
`modules/_OperatorModule.cs`.

**Attribute builtins had two implementations, and the wrong one won.** `modules/BuiltinsModule.cs`
registered `hasattr`/`getattr`/`setattr` with local delegates, shadowing
`PyBuiltinFunction._builtinImplementations`. The winning copies caught *every* exception:

```python
class Boom:
    def __getattr__(self, name): raise ValueError("boom")
```
| expression | CPython | SharpPy before | after |
|---|---|---|---|
| `getattr(Boom(), "anything", "D")` | `ValueError: boom` | `'D'` | `ValueError: boom` |
| `hasattr(Boom(), "anything")` | `ValueError: boom` | `False` | `ValueError: boom` |

CPython's default argument suppresses `AttributeError` only. The `PyBuiltinFunction` copy additionally
carried `if (result is PyNone && default != null) return default;`, which turns `x = None` into a
missing attribute — removed. The duplicates are gone and one implementation remains (same treatment
`abs` got in the P0-18 cluster).

**A name registered twice on one type**: `set.copy` appears both in `set`'s `TypeDict` and as a special
case inside a `GetAttribute` override.

**Dead code that contradicts live code**: `PyString.Upper()`, `PyString.Lower()`,
`PyString.Capitalize()` have no callers; `Capitalize` uses
`CultureInfo.CurrentCulture.TextInfo.ToTitleCase`, i.e. the .NET rule the live `title` implementation
deliberately replaced with CPython's word-boundary rule. Anyone editing the dead copy would "fix" the
wrong thing.

## 3. Argument-error texts: the systemic cause behind P0-29

The scanner counted message shapes across the tree:

| family | occurrences |
|---|---|
| `takes exactly one argument (M given)` | 121 in 23 files |
| `takes no keyword arguments` | 85 in 23 files |
| `expected at most/at least N` | 57 in 16 files |
| `expected N arguments, got M` | 44 in 15 files |
| `missing required argument 'k'` | 33 in 14 files |
| `takes at most N arguments (M given)` | 25 in 10 files |
| `expected exactly N arguments (M given)` | 10 — **a form CPython does not produce** |
| `invalid keyword argument for` | 19 in 11 files |

CPython produces these from two places (`Python/getargs.c` and the clinic-generated parsers), so the
text follows from the declaration. Here every builtin writes its own string, which is why the invented
hybrid family exists. The attribute builtins were converted to the measured CPython forms in this
commit (`hasattr expected 2 arguments, got 3`, `getattr expected at least 2 arguments, got 1`, and one
shared name check `attribute name must be string, not 'int'` instead of four per-function variants);
the rest of the tree is a table-driven sweep, still open as P0-29.

## 4. Deletion semantics, read from CPython rather than guessed

Four different CPython locations produce four different texts; SharpPy had one:

| expression | CPython (source) | SharpPy before |
|---|---|---|
| `delattr(1, "real")` | `AttributeError: attribute 'real' of 'int' objects is not writable` (descrobject.c:255-264, getset with no setter) | `can't delete attribute 'real'` |
| `delattr(1, "__add__")` | `AttributeError: 'int' object attribute '__add__' is read-only` (object.c:1592-1598, no `tp_descr_set`, no instance dict) | same wrong text |
| `delattr("s", "real")` | `AttributeError: 'str' object has no attribute 'real'` (nothing resolved → dict path) | same wrong text |
| `delattr(obj, "__class__")` | `TypeError: can't delete __class__ attribute` (typeobject.c:5712-5719, `object_set_class`) | `AttributeError: ... has no attribute '__class__'` |
| `delattr(int, "__class__")` | `TypeError: cannot set '__class__' attribute of immutable type 'int'` | already correct |
| `delattr(instance, "cls_attr")` | `AttributeError: 'C' object has no attribute 'cls_attr'` (the instance **has** a `__dict__`, so the delete lands there) | already correct |

The last two rows are why the fix is a rule and not a string: the answer depends on whether the object
owns an instance dict and on which slot the resolved name came from.

## 5. New output divergences found by this audit (registered, not yet fixed)

* `str.upper()` / `str.lower()` do nothing above ASCII: `'ß'.upper()` is `'ß'`, CPython `'SS'`;
  `'İ'.lower()` is unchanged, CPython `'i̇'`. `casefold`, `title`, `swapcase`, `istitle` are correct,
  so the gap is the two methods, not the tables.
* `float.fromhex("0x1p3")` returns `0.0` — silently wrong, no exception.
* `bool` is rejected by the int descriptors: `(True).real` raises
  `TypeError: "descriptor 'real' for 'int' objects doesn't apply to ..."`. CPython says `1`, since
  `bool` is an `int` subclass. The message is also wrapped in literal quotes, which no CPython text is.
* `int.__getnewargs__`, `float.__getnewargs__`, `complex.__getnewargs__` are missing (`hasattr` False;
  CPython True) — the pickle surface. `float.real` likewise is absent, so `setattr(1.0, "real", 2)`
  reports "has no attribute" where CPython reports "not writable".
* `operator.pow`/`operator.abs` are a separate implementation from `builtins.pow`/`abs`: they accept
  keywords CPython rejects and their labels lack CPython's module prefix
  (`_operator.pow() takes no keyword arguments`).
* `runtime/PyVM.cs:7025` matches exceptions with `excTypeName.Equals(targetTypeName,
  StringComparison.OrdinalIgnoreCase)` plus a hard-coded "target is `Exception`" rule. Case-folded name
  matching is not CPython semantics (class object / MRO). Not currently observable in the sampled
  cases — `except ValueError` did *not* catch a user `valueerror` — but it is a parallel matching path
  next to the MRO matcher that p07 fixed, and p07 passes only because the MRO path wins first.
* `Encoding.GetEncoding` is called from 4 files outside the codec table
  (`modules/_CodecsModule.cs`, `runtime/PyBinaryTypes.cs`, `core/PyFileContextManager.cs`), so alias
  handling (`utf8`, `utf-8`, `U8`) is inconsistent with `core/PyString.cs`'s `NormalizeCodecName`.

Good news worth stating: 23 of 24 `StringComparison` uses are `Ordinal`, the one exception being the
case-insensitive exception match above; no culture-aware string comparison leaked into the string
methods.

## 6. Is the overall logic close to CPython's?

`tools/bytecode_proximity.py`: opcode enum 166 vs CPython's 129 standard ops, **all 129 present with
identical numbering**, mean sequence similarity **0.845** over 6 code objects (best `select` 1.000,
worst `protected` 0.632 — exception-table-shaped code, the known gap).

Mechanism map, with the places where C# forces a different structure:

| CPython | SharpPy | faithful? |
|---|---|---|
| `tp_as_number/sequence/mapping` slot tables | CLR virtuals + explicit `TypeDict` slot wrappers | only if the wrapper is published; unpublished slots are invisible to subclasses and to `__dict__` (P0-18/P0-28 family) |
| `PyLong` arbitrary precision | `BigInteger` | yes; capacity/sign checks had to be re-implemented (P0-22) |
| refcounts + `tp_free` | CLR GC | different by design; the risk is *when* finalizers run, not what values hold |
| `PyErr_` indicator state | CLR exceptions wrapped in `PythonException` | close, but only if catch sites are narrow — the attribute builtins are the case study |
| `getargs.c` + clinic | per-builtin hand-written checks | **not** faithful: 8 text families, no shared layer (P0-29) |
| static vs heap types (`Py_TPFLAGS_IMMUTABLETYPE`) | guard in `PySetAttribute`/`PyDelAttribute` | yes, incl. the one measured exception (`io.UnsupportedOperation`) |
| codecs registry | .NET `Encoding` + alias table | partial; strict UTF-8 exact, others replace invalid input (P1) |
| `_sre` group numbering | .NET `Regex` + `ToCanonicalPattern` | yes after p47 |
| `_io` in C | `PyIOBase`/`PyStringIO`/`PyBytesIO` in C# | yes after io B1-B4; `getbuffer` still a snapshot, not a live view |
| `ceval` + exception tables | CFG + assembler + VM | closest where straight-line, weakest where try/except tables dominate |

The pattern across every finding above: **CPython states each rule once, in a mechanism; SharpPy
repeats it locally.** Where a rule was localised (types, IO, regex, bytes→bytearray delegation), parity
is good. Where it was copy-pasted, one copy always drifts. The structural remedy is the same in each
case and is now recorded in `docs/known_gaps.md`: one argument-parsing layer, one attribute path, and
deletion of the dead copies rather than leaving them beside the live ones.

## Gates for this commit

* `tests/bughunt/p67_attribute_builtins.py` — 37 cases (arity texts, name texts, swallowing rule,
  four deletion texts, dict-owning asymmetry), CPython vs SharpPy diff **0**.
* `tools/run_bughunt.py`: **72/72**. `tools/evaluate.py`: unchanged `225 / 3 / 2`.
* Godot 4.7.2 mono: Release build 0 errors, `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`.
* `tools/perf_probe.py` flagged `dict_ops +17.3%`, `closure_calls +24.5%`. Nine-round interleaved A/B
  against `119051f`: `closure_calls +23.5%`, `list_comp −33.4%`, **`TOTAL −0.4%`**, and per-workload
  ranges overlap (old closure_calls 0.048–0.062, new 0.045–0.069). Neither workload does an attribute
  lookup, so the flag is not attributable to this change; the suite has no isinstance/ABC-heavy
  workload, which is a coverage gap worth closing before claiming the `LookupAttribute` widening is
  free.
