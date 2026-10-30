# P0-24 item 4 / P0-27: bytes subclasses keep their identity, and bytes publishes its sequence slots.
# Measured against CPython 3.12.15. Constructing class B(bytes) used to return a plain bytes object,
# so the subclass was erased and every declared dunder was unreachable. Separately, bytes had no
# __add__/__mul__/__rmul__ slot wrappers, so the reflected half (3 * b"a") was missing.


def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", str(exc)[:70])


class B(bytes):
    pass


class BUser(bytes):
    def __add__(self, other):
        return "user-add"

    def __len__(self):
        return 99

    def __contains__(self, item):
        return True

    def __iter__(self):
        return iter([1, 2, 3])

    def __getitem__(self, i):
        return "item"

    def __eq__(self, other):
        return "user-eq"

    def __hash__(self):
        return 7

    def __repr__(self):
        return "user-repr"


class C(bytes):
    def __new__(cls, v):
        return bytes.__new__(cls, v)


s("type_name", lambda: type(B(b"xy")).__name__)
s("isinstance_bytes", lambda: isinstance(B(b"xy"), bytes))
s("mro", lambda: [t.__name__ for t in B.__mro__])
s("value_via_base", lambda: bytes(B(b"xy")))
s("len", lambda: len(B(b"xyz")))
s("index", lambda: B(b"xyz")[1])
s("slice", lambda: B(b"xyz")[1:])
s("iter", lambda: list(B(b"xyz")))
s("add", lambda: B(b"a") + b"b")
s("mul", lambda: B(b"ab") * 2)
s("contains", lambda: 120 in B(b"xy"))
s("startswith", lambda: B(b"xy").startswith(b"x"))
s("decode", lambda: B(b"ab").decode())
s("hex", lambda: B(b"\x01\x02").hex())
s("repr", lambda: repr(B(b"xy")))
s("str", lambda: str(B(b"xy")))
s("format", lambda: "%s" % B(b"xy"))
s("hash", lambda: type(hash(B(b"xy"))).__name__)
s("eq_plain", lambda: B(b"xy") == b"xy")
s("eq_sub", lambda: B(b"xy") == B(b"xy"))
s("dict_key", lambda: {B(b"k"): 1}[B(b"k")])
s("new_kw", lambda: B(b"ab", encoding="utf8"))
s("new_int", lambda: B(3))
s("join", lambda: B(b",").join([B(b"a"), B(b"b")]))
s("count", lambda: B(b"aaa").count(b"a"))
s("user_add", lambda: BUser(b"a") + b"b")
s("user_len", lambda: len(BUser(b"abc")))
s("user_contains", lambda: 0 in BUser(b"a"))
s("user_iter", lambda: list(BUser(b"abc")))
s("user_getitem", lambda: BUser(b"abc")[0])
s("user_eq", lambda: BUser(b"a") == b"a")
s("user_hash", lambda: hash(BUser(b"a")))
s("user_repr", lambda: repr(BUser(b"a")))
s("user_int_like", lambda: 3 * BUser(b"a"))
s("subclass_of_sub", lambda: type(type("C", (B,), {})(b"z")).__name__)
s("isinstance_sub", lambda: isinstance(B(b"x"), bytes) and not isinstance(b"x", B))
s("struct_like", lambda: B(b"ab").split(b"a"))

s("bytes_add_bytes", lambda: b"a" + b"b")
s("bytes_add_str", lambda: b"a" + "b")
s("bytes_mul_int", lambda: b"a" * 3)
s("int_mul_bytes", lambda: 3 * b"a")
s("bytes_mul_float", lambda: b"a" * 2.0)
s("bytes_mul_str", lambda: b"a" * "b")
s("bytes_add_int", lambda: b"a" + 1)
s("int_add_bytes", lambda: 1 + b"a")
s("sub_rmul", lambda: 3 * B(b"a"))
s("sub_radd", lambda: b"x" + B(b"a"))
s("sub_ladd", lambda: B(b"a") + b"x")
s("slot_exists", lambda: sorted(n for n in ("__add__", "__radd__", "__mul__", "__rmul__") if n in bytes.__dict__))
s("slot_add_self", lambda: bytes.__add__(b"a", b"b"))
s("slot_mul_kw", lambda: b"a".__mul__(b"b", x=1))
s("slot_add_arity", lambda: b"a".__add__(b"b", b"c"))

# bytes.__new__ has to honour the requested type: this is how an immutable byte wrapper is written,
# and __new__ used to resolve to type.__new__ for bytes.
s("C_user_new_call", lambda: type(C(b"q")).__name__)
s("C_user_new_value", lambda: bytes(C(b"q")))
s("bytes_new_subclass", lambda: type(bytes.__new__(B, b"zz")).__name__)
s("bytes_new_exact", lambda: type(bytes.__new__(bytes, b"zz")).__name__)
s("bytes_new_int", lambda: repr(bytes.__new__(B, 3)))
s("bytes_new_foreign", lambda: bytes.__new__(5, b"x"))
