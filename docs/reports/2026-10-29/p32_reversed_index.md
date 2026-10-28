# p32 — `reversed(bytes/bytearray)` + `tuple.index` / `str` find-family argument parity

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured gaps vs CPython 3.12.15

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `list(reversed(b"abc"))` | `[99, 98, 97]` | `TypeError: 'bytes' object is not reversible` |
| `list(reversed(bytearray(b"ab")))` | `[98, 97]` | `TypeError: 'bytearray' object is not reversible` |
| `type(reversed(b"abc")).__name__` | `'reversed'` | (raised) |
| `len(reversed(b"abc"))` | `TypeError: object of type 'reversed' has no len()` | (raised wrong msg) |
| `(1, 2).index(1, "x")` | `TypeError: slice indices must be integers or have an __index__ method` | `… or None, not 'str'` |
| `(1, 2).index(1, True)` | bool is an int → search from 1 | rejected bool |
| `(1, 2).index(1, start=0)` | `TypeError: tuple.index() takes no keyword arguments` | accepted kwargs → `0` |
| `"xabcy".find("c", "x")` | `TypeError: slice indices must be integers or None or have an __index__ method` | returned `3` (silently ignored bad start) |
| `"aaa".count("a", True)` | `2` | `3` (private count ignored start) |

## Root causes

1. **`reversed`** (`core/PyBuiltin.cs`): `HasGetItem` / `TrySequenceLength`
   recognised only `PyList/PyTuple/PyString/PyRange`, so the sequence protocol on
   `PyBytes`/`PyByteArray` was not seen and the call fell through to "not
   reversible". `PyReversedIterator` already fetches items via
   `GetItem(PyInt)`, and `PyBytes/PyByteArray.GetItem(PyInt)` returns the byte as
   a `PyInt`, so only the two helpers needed to include bytes/bytearray. The
   generic name `"reversed"` and `len()` failure both follow from existing code.

2. **`tuple.index`** (`type/PyTuple.cs`): start/stop parsing used `is PyInt`
   (rejecting `bool`, wrong error text, no `__index__`, no kwargs guard) and the
   `Index` loop did not clamp negatives. Rewrote to `PyObject.TryGetIndex`
   (bool + `__index__`, CPython message `slice indices must be integers or have
   an __index__ method`), reject kwargs (`tuple.index() takes no keyword
   arguments`), and clamp `start`/`stop` CPython-style in `Index`.

3. **`str` find family** (`type/PyString.cs`): non-integer `start`/`stop` were
   silently dropped (`if args[1] is PyInt startInt`, else ignore). There are TWO
   dispatch paths:
   - `index/rfind/rindex/startswith/endswith` resolve through their
     `TypeDict` `PyMethodDescriptor`;
   - **`find` and `count`** resolve through `PyString.GetAttribute` →
     `PyStringMethod(this, "find", Find)` → the **private `Find`/`Count`**
     methods, bypassing the TypeDict descriptor entirely.

   Added `ParseStrStartStop(args, length, out start, out end)` (PyNumber_Index
   semantics; bool + `__index__` accepted, `None` keeps default, else
   `slice indices must be integers or None or have an __index__ method`) and used
   it in all seven descriptors **and** the two private methods. Also added the
   `str.index()` kwargs guard.

## Verification

- `tests/bughunt/p32_reversed_index.py`: stdout diff `0`.
- `tools/run_bughunt.py`: **39/39 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: one run flagged `str_fmt +19% / list_comp +60%`; same
  binary re-measured `str_fmt +3.8%/+5.5%`, `list_comp -1.5%/-0.4%` → machine
  noise (the modified code is argument parsing, not the format/list-comp loops).
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Notes / residuals

- `str.find`/`str.count` remain the only string methods on the private
  `PyStringMethod` fast path; every other method uses the `TypeDict` descriptor.
  Behaviour is now identical, but the dual-path duplication is worth collapsing
  in a later cleanup (delete the private `Find`/`Count` and let both fall through
  to the descriptor) rather than maintaining arg-parsing in two places.
- `bytes`/`bytearray` `find`/`count`/`index` were not in scope here and may share
  the same silent-ignore pattern; deferred to a bytes-specific cluster.
