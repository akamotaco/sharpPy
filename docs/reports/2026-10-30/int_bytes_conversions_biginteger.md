# int.to_bytes / int.from_bytes: a CLR exception leak and two silent wrong answers

Date 2026-10-30. Branch `work/bug-hunt`. Reference CPython 3.12.15. Closes P0-22.
Durable probe: `tests/bughunt/p60_bytes_int_conversions.py` (42 cases, CPython diff 0).

## How it was found

Picking the next item, I wrote a matrix across three ledger entries (`to_bytes` width, `__new__`
labels, a few `bytearray` methods) to measure which to do first. SharpPy died on the **first** line
and printed a CLR stack instead of a Python traceback, so every later line was unreadable:

```
System.OverflowException: Value was either too large or too small for an Int32
   at System.Numerics.BigInteger.op_Explicit(BigInteger value)
   at SharpPy.PyInt.<>c.<InitializeIntDescriptors>b__6_45(...)
```

`(2 ** 70).to_bytes(10, "big")` does not raise in CPython. The delegate did `long value =
(long)intObj.Value`, and an explicit `BigInteger` → `long` cast throws a CLR exception, which is not
a Python exception: nothing catches it, the interpreter unwinds to the host, and the program ends.

That is worth recording as a method point, not just a bug: **a CLR exception erases the test's
visibility**. It killed the script at case 1, so two additional defects sitting on the adjacent
method went unnoticed until the leak was fixed.

## Three defects in one pair of methods

1. **`to_bytes` and width.** Now on `BigInteger`. Capacity follows CPython: compare the magnitude's
   bit count against `length * 8`, and for `signed=True` additionally test the sign bit — measured
   boundary set: `-128` fits one signed byte, `128` does not, `-1` fits, `255` does not, `-129`
   does not. Bytes are produced as two's complement over exactly `length` bytes, which removed the
   separate invert-and-increment pass (and its `length = 0` edge).
   Check order is CPython's: negative-without-signed, then negative length, then capacity.
2. **`int.from_bytes` returned 0 silently** for anything wider than 64 bits.
   `int.from_bytes((2**70).to_bytes(10, "big"), "big")` → CPython `1180591620717411303424`,
   SharpPy `0`. No exception, no warning: the accumulator was a `long` and the shifts fell off the
   end. This is worse than the crash above, because it can silently corrupt a program's results.
3. **`signed` was never parsed, and the top bit was always read as a sign.**
   `int.from_bytes(b'\xff', 'big')` → CPython `255`, SharpPy `-1`. The default in CPython is
   `signed=False`; the code looked at bit 7 of the leading byte unconditionally. The keyword form
   `int.from_bytes(b'\xff','big',signed=True)` also needed the descriptor to be marked as accepting
   keywords.
   Bonus: `byteorder=` as a keyword was ignored by the positional-reading delegate, so
   `int.from_bytes(b'\x01\x00', byteorder='little')` answered 256 instead of 1 — the same
   "flagged `acceptsKwargs` but the delegate only reads positionals" trap recorded under P0-20.

## Argument acceptance

CPython's `int.from_bytes` takes a bytes-like object *or* an iterable of ints. SharpPy required an
exact `PyBytes` and rejected everything else with `'bytes' must be a bytes object, not 'bytearray'`.
Now: `bytes`, `bytearray`, `memoryview`, or any iterable whose items are ints, with CPython's own
messages —

- `int.from_bytes("ab", 'big')` → `TypeError: cannot convert 'str' object to bytes` (a str is refused
  explicitly even though it iterates),
- `int.from_bytes([1, "x"], 'big')` → `TypeError: 'str' object cannot be interpreted as an integer`,
- `int.from_bytes([256], 'big')` → `ValueError: bytes must be in range(0, 256)`.

## Probe note

My first probe file for this was written with a shell here-string, and `printf` turned the literal
`\xff` in the source into a raw 0xFF byte. CPython then refused the file
(`SyntaxError: Non-UTF-8 code starting with '\xff'`) while SharpPy happily ran the mangled bytes and
printed plausible-looking numbers. The project rule exists for exactly this: write probe files with
the file tool, not through a shell.

## Gates

| check | result |
| --- | --- |
| `tests/bughunt/p60_bytes_int_conversions.py` | 42 cases, diff **0** — includes 2**70 / 2**200 round-trips in both byteorders, signed two's complement boundaries, `byteorder=`/`signed=` keywords, bytes-like inputs, int-subclass receivers and elements |
| `tools/run_bughunt.py` | **65/65** matching, mismatches `[]` |
| `tools/evaluate.py` | unchanged `225 / 3 / 2` |
| `tools/perf_probe.py` | no flag |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other` |
