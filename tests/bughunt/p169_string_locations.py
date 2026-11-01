# Triple quotes with escapes, and what the tokenizer reports after them.

x = """a\"""b"""
y = r"""q\"""r"""
z = '''c\''''
w = """
line\
two"""
print(repr(x), repr(y), repr(z), repr(w))


def f():
    """doc
    more""".upper
    return """x
y""".join("ab")


print(f())
a, *rest = [1, 2, 3]
print(a, rest)


def local_star():
    b, *middle, c = range(5)
    return "middle" in globals(), b, middle, c


print(local_star())
print(-1j, -(1 + 2j), +1j, repr(-0j), repr(complex(0, -0.0)), repr(1j), b"'", b'"', b"'\"", bytearray(b"'"))


def lines():
    import sys
    frame = sys._getframe()
    first = frame.f_lineno
    second = (frame
              .f_lineno)
    third = [frame.f_lineno,
             frame.f_lineno]
    return second - first, third[0] - first, third[1] - first


print(lines())
