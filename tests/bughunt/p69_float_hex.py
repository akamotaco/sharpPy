

def s(label, fn):
    try:
        print(label, ascii(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", ascii(str(exc))[:78])


class F(float):
    pass


class MyErr(Exception):
    pass


class G(float):
    def __new__(cls, value=0.0):
        if value == 8.0:
            raise MyErr("hooked")
        return super().__new__(cls, value)


# CPython 3.12 Objects/floatobject.c:1311-1549 float_fromhex.
s("plain_no_point", lambda: float.fromhex("0x1p3"))
s("bare_int", lambda: float.fromhex("1"))
s("two_digits", lambda: float.fromhex("12"))
s("frac", lambda: float.fromhex("0x0.4p0"))
s("pi", lambda: float.fromhex("0x1.921fb54442d18p+1"))
s("neg_pi", lambda: float.fromhex("-0x1.921fb54442d18p+1"))
s("upper", lambda: float.fromhex("0X1.8P1"))
s("lead_trail_ws", lambda: float.fromhex(" \t0x1.8p1\n"))
s("space_before_p", lambda: float.fromhex("0x1.0 p+2"))
s("space_after_sign", lambda: float.fromhex("- 0x1p3"))
s("nbsp", lambda: float.fromhex("\u00a00x1p3"))
s("trailing_junk", lambda: float.fromhex("0x1p3x"))
s("no_exp_digits", lambda: float.fromhex("0x1p"))
s("empty", lambda: float.fromhex(""))
s("only_prefix", lambda: float.fromhex("0x"))
s("only_dot", lambda: float.fromhex("."))
s("underscore", lambda: float.fromhex("1_0"))
s("inf", lambda: float.fromhex("inf"))
s("minus_infinity", lambda: float.fromhex("-INFINITY"))
s("nan", lambda: float.fromhex("+nan"))
s("nan_junk", lambda: float.fromhex("nan)abc"))
s("infinity_junk", lambda: float.fromhex("infinityx"))
s("overflow", lambda: float.fromhex("0x1p10000"))
s("overflow_corner", lambda: float.fromhex("0x1.fffffffffffff8p+1023"))
s("max_float", lambda: float.fromhex("0x1.fffffffffffffp+1023"))
s("underflow", lambda: float.fromhex("0x1p-10000"))
s("subnormal", lambda: float.fromhex("0x0.0000000000001p-52"))
s("min_subnormal", lambda: float.fromhex("0x0.0000000000001p-1074"))
s("round_half_even_down", lambda: float.fromhex("0x1.00000000000008p+0"))
s("round_half_even_up", lambda: float.fromhex("0x1.00000000000018p+0"))
s("long_precision", lambda: float.fromhex("0x1.1999999999999ap+1"))
s("leading_zeros", lambda: float.fromhex("0x000000000000000000001p0"))
s("all_zeros", lambda: float.fromhex("0x0000p+5"))
s("huge_exp_digits", lambda: float.fromhex("0x1p99999999999999999999"))
s("tiny_exp_digits", lambda: float.fromhex("0x1p-99999999999999999999"))
s("too_many_digits", lambda: float.fromhex("0x" + "1" * 3000))

# The classmethod rebuilds through the class it was reached from.
s("subclass_type", lambda: type(F.fromhex("0x1p3")).__name__)
s("subclass_value", lambda: float(F.fromhex("0x1p3")))
s("subclass_hook_sees_value", lambda: G.fromhex("0x1p3").__class__.__name__)

# hex() is the inverse: same value, same exponent spelling.
s("hex_pi", lambda: (3.141592653589793).hex())
s("hex_zero", lambda: (0.0).hex())
s("hex_neg_zero", lambda: (-0.0).hex())
s("hex_inf", lambda: float("inf").hex())
s("hex_nan", lambda: float("nan").hex())
s("hex_max", lambda: float.fromhex("0x1.fffffffffffffp+1023").hex())
s("hex_min_sub", lambda: float.fromhex("0x0.0000000000001p-1074").hex())
s("hex_int_subclass", lambda: (3).hex())


def quiet(label, fn):
    try:
        return ascii(fn())
    except Exception as exc:
        return "EXC " + type(exc).__name__ + ": " + ascii(str(exc))[:78]


# Round-trip: the correctly rounded decoder must reproduce what the encoder produced. The hex
# strings below are fixed literals (SharpPy has no struct module), taken from CPython's float.hex().
HEX = ['0x0.0p+0', '-0x0.0p+0', '0x1.0000000000000p+0', '-0x1.0000000000000p+0', '0x1.999999999999ap-4', '0x0.730d67819e8d2p-1022', '0x1.1ccf385ebc8a0p+1023', '0x0.0000000000001p-1022', '0x1.fffffffffffffp+1023', '0x1.0000000000000p-1022', '0x1.921fb54442d18p+1', '0x1.5555555555555p-1', '0x0.012688b70e62bp-1022', '0x1.ac53a7e04bcdap+66', '-0x1.58139e19f26a6p+18', '-0x1.54f7b4de6bc41p+19', '0x1.26cb3c8c91e6cp+18', '-0x1.a18aeda73d2dcp+19', '0x1.185402347c830p+16', '-0x1.06538aa29bbfep+18', '-0x1.afa444d07dbf8p+19', '0x1.d0bbbb24d45c0p+13', '-0x1.c3aa15dc16262p+19', '-0x1.033250fb807b4p+17', '-0x1.a41024e427481p+19', '-0x1.8fb1bf255fc60p+19', '0x0.0000000000001p-1022', '0x1.0000000000000p-1022', '0x1.0000000000000p+1023', '0x1.fffffffffffffp+0', '0x1.0000000000001p+0']
checked = [quiet("rt", lambda v=v: float.fromhex(v).hex() == v) for v in HEX]
print("roundtrip_all_true", all(c == "True" for c in checked))
print("roundtrip_first", HEX[2], ascii(float.fromhex(HEX[2])))
print("roundtrip_last", HEX[-1], ascii(float.fromhex(HEX[-1])))
