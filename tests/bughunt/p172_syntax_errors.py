cases = ["x = (", "f(a=1, 2)", "print 1", "def f(:\n  pass", "1 = 2", "x + 1 = 2", "if x\n  y", "for x in y\n  z", "class A\n  pass",
 "del f()", "a b", "  x = 1", "if 1:\nx", "(1, 2", "f(**a, *b)", "lambda: (yield) = 1", "def f(a, a): pass", "from x import (a, b", "x = 1 +", "return",
 "[x for x in]", "else: pass", "try:\n  pass\n", "'abc", "\"\"\"abc", "1abc", "x = {1: 2, 3}", "f(x for x in y, 1)", "import a as b.c", "None = 1", "def f(x=1, y): pass",
 "with open(x) as 1: pass", "a := 1", "(a := 1) = 2", "print(a b)", "x = [1, 2", "if x = 1: pass", "while x = 1: pass", "f(a b)", "{1: 2", "try:\n  x\nexcept A, B:\n  y", "except", "async def f(): yield from x", "class C(x for x in y): pass"]
for s in cases:
    try:
        compile(s, "<t>", "exec")
        print(repr(s), "ok")
    except SyntaxError as e:
        print(repr(s), type(e).__name__, e.msg, e.lineno, e.offset, e.end_lineno, e.end_offset, repr(e.text))
