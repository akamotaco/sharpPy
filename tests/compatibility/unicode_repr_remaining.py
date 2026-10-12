"""Formatting audit exposed repr treating surrogate pairs as separate escapes."""
for code in (0x1f600, 0x200b, 0xd800, 0xe000):
    print(code, repr(chr(code)))
