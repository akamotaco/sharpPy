# What Parser/tokenizer.c and pegen_errors.c report, with every position.
cases = ["1 <> 2", "(1 + 2", "(\n1 + 2", "a = (1,\nb = 2\n", "def f(:\n  pass\n", "if x:\n\tpass\n        pass\n", "x = 'a\n",
    "print(1\nx = 2\n", "\\", "\\ x", "x = \\", "1__0", "0_", "0xg", "0b2", "0o8", "012", "1e", "1_e1", "é = 1\nñ€ = 2\n", "x = $", "x = ?",
    "\x7f", "a = 1;\n  b = 2\n", "class C:\npass\n", "f'{a b}'", "f'{'", "f'}'", "f'{a!x}'", "f'{a!}'", "f'{a:{b:{c:{d}}}}'", "f'{}'",
    "f'{a=!r:>3}'[", "b'é'", "'\\x'", "b'\\x'", "'\\N{foo}'", "'\\777'", "('a' b'c')", "print 1", "return", "x = [\n1,\n2\n",
    "é = (", "'é' 1 (", "if 1:\n  x\n y\n", "f'''{\n", "f'''a", "'''a", "x = f'{a", "f'{a:", "f'{a:x", "f\"{f'{f\"{1}\"}'}\"", "f'{a\n}'", "f'''{a\n}'''", "f'{a:\n}'",
    "f'{a!r\n}'", "f'{a !r}'", "f'{a! r}'", "f'{lambda x: 1}'", "f'{a:{b}'", "f'{(}'", "f'{)}'", "f'{]}'", "f'{a)}'", "f'{{}'", "f'{1:{2}{{'", "1if 1else 2", "0x1for x in y", "1_000.0_1e1_0j", "1.e5", "1.j", ".5", "...", "..", "a.b", "1..real",
    "\ufeffx = 1", "x\u00a0= 1", "x = \u20ac", "a\u20acb = 1", "\U0001f600 = 1", "x = '\U0001f600' $", "\U0001f600 $", "'\U0001f600' + (", "def f():\n\tx\n        y\n", "def f():\n        x\n\ty\n", "\tx\n", "  x\n", "if 1:\n    x\n  y\n",
    "\x00", "x = 1\n\x00", "# coding: x\n", "\f\n", "  \n", "\\\n", "\\\nx", "x \\\n", "if 1:\n\\\n  x\n", " \\\n x", "a = 1\n\\", "(\\", "'\\", "'''\\"]
for s in cases:
    try:
        compile(s, "<s>", "exec")
        print(repr(s), "OK")
    except SyntaxError as e:
        print(repr(s), type(e).__name__, e.msg, e.lineno, e.offset, e.end_lineno, e.end_offset, repr(e.text))
    except Exception as e:
        print(repr(s), type(e).__name__, e)
for s, m in [("1\n2\n", "single"), ("x=1;y=2\n", "single"), ("if 1:\n  x\n", "single"), ("1 #c\n#d\n", "single"), ("1\n", "eval"), ("1", "eval"), ("", "exec"), ("  ", "exec"), ("\n", "single"), ("", "single"), ("if 1:\n  x", "single"), ("for i in x:\n", "single"), ("1 +", "eval"), ("(", "eval"), ("", "eval")]:
    try:
        compile(s, "<s>", m)
        print(repr(s), m, "OK")
    except SyntaxError as e:
        print(repr(s), m, type(e).__name__, e.msg, e.lineno, e.offset, e.end_lineno, e.end_offset, repr(e.text))
