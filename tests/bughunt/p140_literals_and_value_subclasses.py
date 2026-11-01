import enum
import textwrap

a = """\
x
"""
print(repr(a))
print(repr("""\
    b
    """))
b = "a\
b"
print(repr(b), repr(b"c\
d"), repr(r"e\
f"), repr(f"g\
h{1}"), repr(f"""\
i{2}
"""))
print(repr(textwrap.dedent("""\
    d
      e
    """)))


class L(enum.IntFlag):
    A = 1
    B = 2


class E(enum.IntEnum):
    X = 1


print(list(L), [L.A], repr(L.A), (L.A,), {L.A: 1}, str(L.A), f"{L.A}", f"{L.A!r}", L.A | L.B)
print(list(E), repr(E.X), str(E.X), f"{E.X}", E.X + 1, [E.X])


class M(int):
    def __repr__(self):
        return "M!" + int.__repr__(self)


class N(int):
    def __str__(self):
        return "N!"


class O(int):
    pass


print([M(1)], (M(2),), {M(3): M(4)}, repr(M(5)), str(M(6)), f"{M(7)}", f"{M(8)!r}", "%s %r %d" % (M(9), M(9), M(9)))
print(str(N(1)), repr(N(2)), [N(3)], f"{N(4)}", f"{N(5)!s}", "%s" % N(6))
print(repr(O(1)), str(O(2)), [O(3)])


class S(str):
    def __repr__(self):
        return "S!"


class T(str):
    def __str__(self):
        return "T!"


print([S("a")], (S("b"),), {S("c"): S("d")}, repr(S("e")), str(S("f")))
print([T("a")], str(T("b")), repr(T("c")), f"{T('d')}", T("e") + "x")


class F(float):
    def __repr__(self):
        return "F!" + float.__repr__(self)


class G(float):
    def __str__(self):
        return "G!"


print([F(1)], (F(2),), repr(F(3)), str(F(4)), f"{F(5)}", [G(1)], str(G(2)), repr(G(3)))
print(int.__repr__(True), True, [True], repr(False))


class B(bytes):
    def __repr__(self):
        return "B!"


print([B(b"a")], repr(B(b"b")))
type Position = tuple[float, float]


def move(pos: Position) -> Position:
    return pos


print(move((1.0, 2.0)), move.__annotations__["pos"] is not None)
