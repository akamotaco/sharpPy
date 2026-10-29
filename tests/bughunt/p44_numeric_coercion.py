# p44: numeric coercion dunders. CPython Objects/abstract.c PyNumber_Long consults
# nb_int (__int__) **before** nb_index, while PyNumber_Float consults nb_float then
# nb_index and never __int__ (measured: int(Both()) == 70, float(Both()) == 30.0).
# float() also accepts every buffer text form, rejects keywords, and its text parser
# follows CPython: PEP 515 underscores only between digits, culture-independent digits,
# and failure messages that report the exporting object (float(b'x') says "b'x'").
def s(l, f):
    try:
        print(l, repr(f()))
    except Exception as e:
        print(l, "EXC", type(e).__name__, str(e))



class WithInt:
    def __int__(self):
        return 7


class WithIndex:
    def __index__(self):
        return 3


class Both:
    def __int__(self):
        return 70

    def __index__(self):
        return 30


class WithFloat:
    def __float__(self):
        return 1.5


class IntReturnsFloat:
    def __int__(self):
        return 2.5


class IntReturnsStr:
    def __int__(self):
        return "x"


class IndexReturnsStr:
    def __index__(self):
        return "x"


class FloatReturnsInt:
    def __float__(self):
        return 5


class FloatReturnsStr:
    def __float__(self):
        return "x"


class Raises:
    def __int__(self):
        raise ValueError("boom-int")

    def __float__(self):
        raise KeyError("boom-float")


class IndexBig:
    def __index__(self):
        return 2 ** 70


class IntReturnsBool:
    def __int__(self):
        return True


class FloatReturnsBool:
    def __float__(self):
        return True



class MyIntSub(int):
    pass


class MyFloatSub(float):
    pass


class Plain:
    pass


# --- int() dispatch order
s("int.WithInt", lambda: int(WithInt()))
s("int.WithIndex", lambda: int(WithIndex()))
s("int.Both", lambda: int(Both()))
s("int.WithFloat", lambda: int(WithFloat()))
s("int.IndexBig", lambda: int(IndexBig()))
s("int.IndexBig.type", lambda: type(int(IndexBig())).__name__)
s("int.IntReturnsFloat", lambda: int(IntReturnsFloat()))
s("int.IntReturnsStr", lambda: int(IntReturnsStr()))
s("int.IndexReturnsStr", lambda: int(IndexReturnsStr()))
s("int.IntReturnsBool", lambda: int(IntReturnsBool()))
s("int.raises", lambda: int(Raises()))
s("int.Plain", lambda: int(Plain()))
s("int.None", lambda: int(None))

# --- float() dispatch order
s("float.WithFloat", lambda: float(WithFloat()))
s("float.WithIndex", lambda: float(WithIndex()))
s("float.WithInt", lambda: float(WithInt()))
s("float.Both", lambda: float(Both()))
s("float.IndexBig", lambda: float(IndexBig()))
s("float.FloatReturnsInt", lambda: float(FloatReturnsInt()))
s("float.raises_float", lambda: float(Raises()))
s("float.Plain", lambda: float(Plain()))
s("float.None", lambda: float(None))

s("float.FloatReturnsStr", lambda: float(FloatReturnsStr()))
s("float.FloatReturnsBool", lambda: float(FloatReturnsBool()))
s("int.sub_int", lambda: int(MyIntSub(4)))
# deferred: see docs/known_gaps.md (float-subclass payload / __trunc__ / repr address)
# deferred: see docs/known_gaps.md (float-subclass payload / __trunc__ / repr address)

# --- built-in numbers still fast
s("int.exact", lambda: int(5))
s("int.bool", lambda: int(True))
s("int.neg", lambda: int(-17))
s("int.float", lambda: int(3.7))
s("int.floatneg", lambda: int(-3.7))
s("float.exact", lambda: float(1.5))
s("float.int", lambda: float(3))
s("float.bool", lambda: float(False))

# --- strings / bytes / bases
s("int.str", lambda: int("12"))
s("int.str_ws", lambda: int("  12 \n"))
s("int.str_plus", lambda: int("+12"))
s("int.str_bad", lambda: int("1x"))
s("int.str_empty", lambda: int(""))
s("int.bytes", lambda: int(b"12"))
s("int.bytearray", lambda: int(bytearray(b"-12")))
s("int.hex", lambda: int("0x1f", 16))
s("int.hex_prefixed_auto", lambda: int("0x1f", 0))
s("int.bin_space", lambda: int("1 0 1", 2))
s("int.base1", lambda: int("10", 1))
s("int.base37", lambda: int("10", 37))
s("int.base_neg", lambda: int("10", -1))
s("int.base_bad_digit", lambda: int("12", 2))
s("int.explicit_base_nonstr", lambda: int(3.7, 10))
s("int.explicit_base_obj", lambda: int(WithInt(), 10))
s("int.noargs", lambda: int())
s("int.kw_x", lambda: int(x=1))
s("int.kw_base", lambda: int("10", base=2))
s("float.str", lambda: float("1.5"))
s("float.str_ws", lambda: float("  1.5 "))
s("float.bytes", lambda: float(b"2.25"))
s("float.hex", lambda: float("0x1.8p1"))
s("float.inf", lambda: float("inf"))
s("float.bad", lambda: float("x"))
s("float.empty", lambda: float(""))
s("float.kw_x", lambda: float(x=1))

s("op.index_Plain", lambda: __import__("operator").index(Plain()))
s("op.index_WithInt", lambda: __import__("operator").index(WithInt()))
s("op.index_float", lambda: __import__("operator").index(1.5))
s("op.index_str", lambda: __import__("operator").index("1"))


# --- float() text forms and signature
class Big:
    def __index__(self): return 10**400
class Cpx:
    def __complex__(self): return 1+2j
s("float_big", lambda: float(10**400))
s("float_big_idx", lambda: float(Big()))
s("float_two", lambda: float("1", 2))
s("float_kw", lambda: float(x=1))
s("float_ba", lambda: float(bytearray(b"1.25")))
s("float_mv", lambda: float(memoryview(b"1.5")))
s("float_tabs", lambda: float("1_0.5"))
s("complex_dunder", lambda: complex(Cpx()))
s("int_noargs_kw", lambda: int(base=10))
s("int_str_kw", lambda: int("10", 2, 3))
class Trunc:
    def __trunc__(self): return 9
# deferred: see docs/known_gaps.md (float-subclass payload / __trunc__ / repr address)
s("float_nan_str", lambda: float("  NaN  "))
s("int_underscore", lambda: int("1_000"))


# --- buffer text forms and underscore rules


s("b_bad", lambda: float(b"x"))
s("ba_bad", lambda: float(bytearray(b"x")))
# deferred: see docs/known_gaps.md (float-subclass payload / __trunc__ / repr address)
s("b_empty", lambda: float(b""))
s("b_ws", lambda: float(b" 1.5 \n"))
s("b_unde", lambda: float(b"1_0.5"))
s("s_lead_us", lambda: float("_1"))
s("s_dbl_us", lambda: float("1__0"))
s("s_trail_us", lambda: float("1_"))
s("s_exp_us", lambda: float("1e1_0"))
s("s_us_after_pt", lambda: float("1._5"))
s("s_dot_us", lambda: float(". 5"))
s("int_us_lead", lambda: int("_1"))
s("int_b_unde", lambda: int(b"1_0"))
s("b_inf", lambda: float(b"INF"))
s("mv_nd", lambda: float(memoryview(b"1.5").cast("I", (3,))))
