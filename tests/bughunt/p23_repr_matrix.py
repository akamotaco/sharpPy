# p23: callable repr matrix against CPython 3.12.
# Addresses are volatile, so every repr is normalized (0xHEX -> 0xADDR) before
# printing; only the repr SHAPE is compared. Categories deferred to a later
# cluster (documented in the report): method-wrapper name binding, the C
# slot-wrapper vs method_descriptor classification for SharpPy-stored slots
# (list.__len__), super() type/repr, per-source iterator type names
# (list_iterator/list_reverseiterator), and the type-object getattr message.
import re

ADDR = re.compile(r"0x[0-9a-fA-F]+")
def R(label, fn):
    try:
        print(label, "::", ADDR.sub("0xADDR", repr(fn())))
    except Exception as e:
        print(label, ":: EXC", type(e).__name__, e)

# --- module-level builtins: <built-in function NAME> + object attributes ---
R("len", lambda: len)
R("print", lambda: print)
R("abs", lambda: abs)
R("len.name", lambda: len.__name__)
R("len.qualname", lambda: len.__qualname__)
R("len.class", lambda: len.__class__)
R("len.hasclass", lambda: hasattr(len, "__class__"))
R("dir-has-dir", lambda: "__dir__" in dir(len))
R("class-str", lambda: str(len.__class__))

# --- method_descriptor: <method 'NAME' of 'TYPE' objects> ---
R("str.upper", lambda: str.upper)
R("str.format", lambda: str.format)
R("dict.get", lambda: dict.get)
R("list.append", lambda: list.append)
R("dict.__getitem__", lambda: dict.__getitem__)

# --- wrapper_descriptor (slot): <slot wrapper 'NAME' of 'TYPE' objects> ---
R("str.__add__", lambda: str.__add__)
R("object.__init__", lambda: object.__init__)
R("str.__eq__", lambda: str.__eq__)

# --- bound builtin method: <built-in method NAME of TYPE object at 0xID> ---
R("bound list.append", lambda: [].append)
R("bound dict.get", lambda: {}.get)
R("bound dict.__getitem__", lambda: {}.__getitem__)
R("bound str.upper", lambda: "a".upper)
R("bound-str-self", lambda: "a".upper.__self__ == "a")

# --- user functions / methods (qualname + bound form) ---
def f(x, y=1): pass
class C:
    def m(self): pass
    @classmethod
    def cm(cls): pass
    @staticmethod
    def sm(): pass
    @property
    def pr(self): return 0
    def __repr__(self): return "<C>"

R("user f", lambda: f)
R("C.m", lambda: C.m)
R("C().m", lambda: C().m)
R("C.cm", lambda: C.cm)
R("C().cm", lambda: C().cm)
R("C.sm", lambda: C.sm)
R("C().sm", lambda: C().sm)
R("C.pr", lambda: C.pr)
R("bound-m.__self__", lambda: C().m.__self__)
R("bound-m.__func__", lambda: C().m.__func__)
R("bound-m.name", lambda: C().m.__name__)

# nested function / lambda qualname handling
def outer():
    def inner(): pass
    return inner
R("lambda", lambda: (lambda: 0))
R("outer.inner", lambda: outer())

# --- lazy iterator / sequence object reprs ---
R("map", lambda: map(str, [1]))
R("filter", lambda: filter(None, [1]))
R("zip", lambda: zip([1]))
R("range", lambda: range(3))
R("enumerate", lambda: enumerate([1]))
R("memoryview", lambda: memoryview(b"ab"))
R("sliced-range", lambda: range(1, 9, 2))
