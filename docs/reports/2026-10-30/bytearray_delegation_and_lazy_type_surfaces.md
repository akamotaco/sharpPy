# p64 / p65 — bytearray delegates to bytes, and why bytes looked empty

Date: 2026-10-30 · branch `work/bughunt` · reference CPython 3.12.15

## What was wrong

`bytearray` was missing 32 methods that CPython's `bytearray` carries:

```text
capitalize center decode expandtabs fromhex hex isalnum isalpha isascii isdigit islower
isspace istitle isupper join ljust lower lstrip maketrans partition replace rjust rpartition
rsplit rstrip split splitlines strip swapcase title upper zfill
```

`bytearray(b"ab").upper()` was an `AttributeError`. p40/p41 had closed the search/query family
(`find`, `count`, `index`, `startswith`, …) and nothing else. Two CPython facts had to be reproduced
at once:

* non-mutating methods return **bytearray**, not bytes;
* `split`, `rsplit`, `splitlines`, `partition`, `rpartition`, `join` produce bytearrays for the
  **elements** too, e.g. `bytearray(b"a,b").split(b",") == [bytearray(b"a"), bytearray(b"b")]`.

## Delegation, not a second implementation

Registering bytes' algorithms a second time for bytearray is the shape this project rejects, and it
is 400+ lines of drift waiting to happen. A bytearray method instead builds a `PyBytes` proxy over
its own buffer, calls the descriptor bytes already publishes through
`PyMethodDescriptor.CallWithReceiver`, and converts the result back — `bytes` → `bytearray`,
recursively through lists and tuples. Fixing `bytes.upper` now fixes `bytearray.upper`.

One measured exception: `maketrans` returns **bytes** from both types (CPython's own asymmetry),
while `fromhex` returns bytearray.

## Bugs the delegation exposed on the bytes side

Delegating means bytearray inherits whatever bytes actually does, so these had to be fixed in
`type/PyBytes.cs` rather than patched around in bytearray:

| item | before | after (matches CPython) |
|---|---|---|
| `bytes.hex(sep)` | argument ignored | `'01:ab'`; wrong length → `sep must be length 1.`; non-buffer → `object of type 'int' has no len()` |
| `bytes.join` | only a `PyList` of `PyBytes` | any iterable; elements must be bytes/bytearray/memoryview (`str` → `sequence item 0: expected a bytes-like object, str found`); non-iterable → `can only join an iterable` |
| `bytes.split`, `rsplit` | keywords and a third positional ignored | `sep`/`maxsplit` by name; `split() takes at most 2 arguments (3 given)` |
| `bytes.expandtabs` | keywords ignored | `tabsize=` |
| `bytes.fromhex` | whitespace stripped, positionless message | scans the original string; `non-hexadecimal number found in fromhex() arg at position 3`; a dangling nibble reports the string length |
| `bytes.maketrans` | `maketrans() takes exactly 2 arguments (1 given)` | `maketrans expected 2 arguments, got 1` |

`hex` accepts a `str` separator (`hex(":")`) while `split` refuses one
(`a bytes-like object is required, not 'str'`). That asymmetry is CPython's, so there are now two
named extractors, `StringOrBufferOrNull` and `BufferOrNull`, instead of one convenient helper.

## Two traps worth recording

**A classmethod binding eats a positional argument.** `bytearray.fromhex` registered as a
`PyClassMethodDescriptor` produces a `PyCMethod` from `Get()`, which inserts the class as the first
positional argument. The delegated bytes implementation counts it:
`fromhex() takes exactly one argument (2 given)`. Registering a plain callable fixes it, and
CPython shows `type(bytearray.fromhex) is builtin_function_or_method` either way, so nothing is lost.

**Generic argument errors are qualified with the defining type; handwritten ones are not.** CPython
gives `bytearray.upper() takes no arguments (1 given)` (generic parser) but
`strip expected at most 1 argument, got 2` (handwritten). A delegation reports `bytes.…`. So the
wrapper produces only the measured qualified families (`QualifiedArity`, `NamedKeywordMethods`) and
lets the delegated implementation report everything else. Both directions have to hold at once:
`bytearray.upper(x=1)` must be refused with the bytearray prefix, while
`bytearray.decode(encoding="utf-8")` must reach the implementation.

## The bug found on the way: type surfaces registered by CLR static constructors

While wiring the delegation, `PyType.BytesType.TypeDict` looked empty from C# — and from Python:

```text
bytes.TypeDict entries   1   (same runtime: str 63, list 33, int 57)
dir(bytes)               24  (CPython 78)
hasattr(bytes, "upper")  False
getattr(bytes, "fromhex")  AttributeError: type object 'bytes' has no attribute 'fromhex'
b"a".upper()             'AB'      (works)
```

Every builtin type attaches its TypeDict entries from a CLR static constructor, which runs the first
time the **CLR class** is touched, not the first time the **Python type** is used. A script that
named `bytes` without ever creating a bytes object therefore saw a nearly empty type, and the same
holds for any type whose class is not touched early. `hasattr`, `getattr(obj, name, default)`,
`dir()`, `type.__dict__` and structural ABC checks all read that surface, while ordinary dispatch
uses the same descriptors directly — which is why `b"a".upper()` always worked and why this survived.
It is also the reason `collections.abc` checks had to be re-implemented as native capability
primitives earlier.

`PyType`'s static constructor now forces the relevant class constructors with
`RuntimeHelpers.RunClassConstructor`, in the order the surfaces depend on: `str` and the numeric
types first (codecs and error texts use them), then `bytes`, then `bytearray` (whose delegated
surface is copied out of bytes' TypeDict), and `PyClass` last.

After the fix: `dir(bytes)` 24 → 70, `dir(bytearray)` → 80, `hasattr(bytes, "upper")` True,
`getattr(bytes, "fromhex")` → `builtin_function_or_method`.

**Still open, recorded as P0-28:** some types have a static constructor that does not publish their
surface at all — `frozenset`, `complex`, `memoryview`, the dict views, the iterator types, and
`bytes.__getnewargs__` in `dir()`. The operations themselves are correct
(`frozenset([1]).union({2})`, `complex(1,2).conjugate()`, `memoryview(b"ab").cast("B")`); only the
introspection APIs disagree.

## Gates

* `tests/bughunt/p64_bytearray_bytes_backed.py` — 127 lines, values, element types, class-level
  helpers and the message families, CPython vs SharpPy diff **0**.
* `tests/bughunt/p65_registration_order.py` — reads type surfaces before any instance exists, diff **0**.
* `tools/run_bughunt.py`: **70/70 matching**; `tools/evaluate.py`: unchanged
  `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
* Godot 4.7.2 mono, source-included Release build: replay `82 matched, 0 other`, and the default
  probe `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`, `SHARPPY_GODOT_PASS`.
* `tools/perf_probe.py` flagged `str_fmt +23.1%` and `list_comp +21.8%` three times; a 9-round
  interleaved A/B against `c260a27` gave `str_fmt -2.3%`, `list_comp -6.9%`, **TOTAL -0.8%**, with
  both binaries bimodal on `list_comp` (0.053–0.122 s). Judged noise, consistent with the earlier
  P0-25/P0-27 measurements of the same two workloads.
