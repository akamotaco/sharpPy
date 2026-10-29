# p43: a metaclass __str__/__repr__ governs str()/repr()/format() of class objects,
# because CPython dispatches PyObject_Str/Repr on type(obj), which for a class is the
# metaclass. type.__str__ is object.__str__ in CPython, so it follows tp_repr too.
import re


def norm(text):
    # Instance identity addresses are volatile; p23/p39 normalize them the same way.
    return re.sub(r"0x[0-9A-Fa-f]+", "0xADDR", text)


def s(l, f):
    try:
        print(l, norm(repr(f())))
    except Exception as e:
        print(l, "EXC", type(e).__name__, norm(str(e)))


class M(type):
    def __str__(cls):
        return "META-str:" + cls.__name__

    def __repr__(cls):
        return "META-repr:" + cls.__name__


class C(metaclass=M):
    pass


class D(C):
    pass


class M2(M):
    pass


class E(metaclass=M2):
    pass


class M3(type):
    pass


class G(metaclass=M3):
    pass


class NStr(type):
    def __str__(cls):
        return 5


class CN(metaclass=NStr):
    pass


class RStr(type):
    def __repr__(cls):
        return 7


class CR(metaclass=RStr):
    pass


class SStr(type):
    def __str__(cls):
        raise ValueError("boom")


class CS(metaclass=SStr):
    pass


class Plain:
    pass


s("str.C", lambda: str(C))
s("repr.C", lambda: repr(C))
s("fstr.C", lambda: f"{C}")
s("fmt.C", lambda: format(C))
s("fmtspec.C", lambda: format(C, ">20"))
s("%s.C", lambda: "%s" % C)
s("str.D", lambda: str(D))
s("repr.D", lambda: repr(D))
s("str.E", lambda: str(E))
s("repr.E", lambda: repr(E))
s("str.G", lambda: str(G))
s("repr.G", lambda: repr(G))
s("str.Plain", lambda: str(Plain))
s("repr.Plain", lambda: repr(Plain))
s("str.type", lambda: str(type))
s("repr.type", lambda: repr(type))
s("str.int", lambda: str(int))
s("type.__str__", lambda: type.__str__(C))
s("type.__repr__", lambda: type.__repr__(C))
s("M.__str__", lambda: M.__str__(C))
s("M.__str__self", lambda: M.__str__(M))
s("str.M", lambda: str(M))
s("repr.M", lambda: repr(M))
s("str.M3", lambda: str(M3))
s("nonstr.__str__", lambda: str(CN))
s("nonstr.fstr", lambda: f"{CN}")
s("nonstr.__repr__", lambda: repr(CR))
s("raise.__str__", lambda: str(CS))
s("raise.fmt", lambda: format(CS))
# deferred: P1 object.__str__/__repr__ materialization on class objects
# deferred: P1 object.__str__/__repr__ materialization on class objects
s("inst.str", lambda: str(C()))
s("inst.repr", lambda: repr(C()))
s("print.C", lambda: print(C))
s("list.C", lambda: [C])
s("dictkey.C", lambda: {C: 1})
s("tuple.C", lambda: (C,))
s("C.__class__.__str__", lambda: str(C.__class__))
