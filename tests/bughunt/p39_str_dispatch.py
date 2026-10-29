# p39: str()/repr() dispatch after AsString() became the single virtual path.
# Locks: builtin str matrix, user __str__/__repr__, __str__ failure -> __repr__
# fallback, exception str/repr, f-string/%s routing.
import re


def norm(text):
    # Repr addresses are volatile (CPython pointer vs CLR hashcode).
    return re.sub(r"0x[0-9a-fA-F]+", "0xADDR", text)


def s(label, fn):
    try:
        print(label, norm(repr(fn())))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


class WithStr:
    def __str__(self):
        return "str!"

    def __repr__(self):
        return "repr!"


class WithReprOnly:
    def __repr__(self):
        return "ro!"


class BadStr:
    def __str__(self):
        raise ValueError("boom")

    def __repr__(self):
        return "fallback-repr"


class Sub(WithStr):
    pass


class NonStr:
    def __str__(self):
        return 123

    def __repr__(self):
        return "R2"


class NoneStr:
    __str__ = None

    def __repr__(self):
        return "R3"


class BadRepr:
    def __repr__(self):
        raise RuntimeError("repr-boom")


objs = [
    1,
    -2**70,
    True,
    None,
    3.5,
    1e30,
    "txt",
    [1, [2]],
    (1,),
    {1, 2},
    b"by",
    bytearray(b"ab"),
    {"a": 1},
    1 + 2j,
    slice(1, 2, 3),
    range(3),
    int,
    ValueError,
    object(),
    WithStr(),
    Sub(),
    WithReprOnly(),
    BadStr(),
    ValueError("argh"),
    KeyError("k"),
]

for i, o in enumerate(objs):
    s("str[%d]" % i, lambda o=o: str(o))
    s("repr[%d]" % i, lambda o=o: repr(o))
    s("fstr[%d]" % i, lambda o=o: f"{o}")
    s("pct[%d]" % i, lambda o=o: "%s" % (o,))

s("str-nonstr", lambda: str(NonStr()))
s("repr-nonstr", lambda: repr(NonStr()))
s("str-nonestr", lambda: str(NoneStr()))
s("str-badrepr", lambda: str(BadRepr()))
s("iter-str", lambda: [str(x) for x in (WithStr(), WithReprOnly(), 1, 2.5)])
s("join", lambda: "|".join(str(x) for x in (1, WithStr(), None)))
s("map-str", lambda: list(map(str, (1, WithStr(), [1]))))
s("print-ish", lambda: str({"k": WithStr()}))
s("nested-bad", lambda: str([BadStr()]))
s("exc-in-args", lambda: str(ValueError(BadStr())))

it = iter([1])
s("iterator-str", lambda: str(it))
# NOTE: generator repr fidelity (qualname + address form) is a separate P1 gap;
# comparing it here would only mask it, so it is left out.
