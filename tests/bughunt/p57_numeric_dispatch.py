# Numeric dispatch for builtin subclasses, and one implementation per operation.
# Measured against CPython 3.12.15. Three root causes are covered here:
#  - abs had four separate implementations (builtins, operator, math, core) with different type
#    tests, so abs(FSub(2.5)) failed while float.__abs__(FSub(2.5)) worked. math.abs itself does
#    not exist in CPython.
#  - int and float published fewer numeric slots than CPython does, so a subclass reached through a
#    dunder found nothing.
#  - bin/hex/oct/operator.index each re-implemented PyNumber_Index and followed a user __index__,
#    while CPython keeps long's value slot for int subclasses.
# One attribute-precedence fix belongs to the same cluster: inside a single MRO frame the class's
# own namespace must win over the inherited/native merged view, otherwise an inherited slot wrapper
# shadows a user definition (i.__index__() answered 5 where CPython answers 3).


class FSub(float):
    pass


class ISub(int):
    pass


class WithAbs:
    def __abs__(self):
        return "custom"


class AbsNotImplemented:
    def __abs__(self):
        return NotImplemented


class IO(int):
    def __index__(self):
        return 3


def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__, str(exc)[:60])


import math
import operator

# abs: one meaning, __abs__ first, result returned as-is
s("math_has_abs", lambda: hasattr(math, "abs"))
s("abs_fsub", lambda: abs(FSub(-2.5)))
s("abs_fsub_type", lambda: type(abs(FSub(-2.5))).__name__)
s("abs_isub", lambda: abs(ISub(-5)))
s("abs_user", lambda: abs(WithAbs()))
s("abs_notimpl_is_sentinel", lambda: abs(AbsNotImplemented()) is NotImplemented)
s("abs_bool", lambda: abs(False))
s("abs_complex", lambda: abs(3 + 4j))
s("abs_str", lambda: abs("x"))
s("abs_noargs", lambda: abs())
s("operator_fsub", lambda: operator.abs(FSub(-2.5)))
s("operator_user", lambda: operator.abs(WithAbs()))
s("operator_notimpl", lambda: operator.abs(AbsNotImplemented()) is NotImplemented)
s("operator_arity", lambda: operator.abs())
s("abs_raises", lambda: abs(type("R", (), {"__abs__": lambda self: (_ for _ in ()).throw(ValueError("boom"))})()))

# Numeric slots CPython publishes and SharpPy did not. Compared by behaviour, not by repr label:
# CPython calls int.__index__ a slot wrapper but int.__floor__ a method, so the repr text is not
# the interesting part - being present and reading the payload is.
s("int_index_call", lambda: int.__index__(7))
s("int_floor_call", lambda: int.__floor__(-7))
s("int_trunc_call", lambda: int.__trunc__(-7))
s("int_ceil_call", lambda: int.__ceil__(7))
s("int_int_call", lambda: int.__int__(7))
s("int_float_call", lambda: int.__float__(7))
s("float_add_call", lambda: float.__add__(1.5, 2))
s("float_floordiv_call", lambda: float.__floordiv__(7.5, 2))
s("float_pow_call", lambda: float.__pow__(2.0, 3))
s("float_has_index", lambda: hasattr(float, "__index__"))

# PyNumber_Index: an int subclass keeps the value slot
s("attr_index", lambda: IO(5).__index__())
s("slot_index", lambda: int.__index__(IO(5)))
s("operator_index", lambda: operator.index(IO(5)))
s("bin_intsub", lambda: bin(IO(5)))
s("hex_intsub", lambda: hex(IO(255)))
s("oct_intsub", lambda: oct(IO(8)))
s("operator_index_arity", lambda: operator.index())
s("operator_index_str", lambda: operator.index("x"))
s("bin_floatsub", lambda: bin(FSub(2.5)))
s("user_index_class", lambda: operator.index(type("C", (), {"__index__": lambda self: 4})()))

# % formatting accepts numeric subclasses by value
s("pct_d_intsub", lambda: "%d" % ISub(5))
s("pct_f_floatsub", lambda: "%.3f" % FSub(2.5))
s("pct_x_intsub", lambda: "%x" % ISub(255))
s("pct_d_floatsub", lambda: "%d" % FSub(2.7))
s("pct_x_float", lambda: "%x" % 2.5)
s("pct_d_str", lambda: "%d" % "x")
