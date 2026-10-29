# P0-18 / P0-16: float publishes its numeric slots, so a float subclass computes.
# CPython's float_as_number (Objects/float.c, wired through Objects/slotdefs.c nb_reflected)
# exposes __radd__/__rsub__/... and __abs__/__floor__/__trunc__/__ceil__ as slot wrappers that read
# the value payload. SharpPy had none of them registered: the VM only had hard-coded CLR paths for
# an exact PyFloat, so a subclass carrying a real float value still raised TypeError.
# Measured against CPython 3.12.15. Registered remainders: % formatting, divmod(), abs().


class FSub(float):
    pass


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:58])


x = FSub(2.5)
s("add_left", lambda: x + 1)
s("add_right", lambda: 1 + x)
s("sub_left", lambda: x - 1)
s("sub_right", lambda: 1 - x)
s("mul", lambda: x * 2)
s("mul_rev", lambda: 2 * x)
s("truediv", lambda: x / 2)
s("truediv_rev", lambda: 1 / x)
s("floordiv", lambda: x // 2)
s("floordiv_rev", lambda: 7 // x)
s("mod", lambda: x % 2)
s("pow", lambda: x ** 2)
s("neg", lambda: -x)
s("pos", lambda: +x)
s("result_type", lambda: type(1 + x).__name__)
s("floor_method", lambda: x.__floor__())
s("trunc_method", lambda: x.__trunc__())
s("ceil_method", lambda: x.__ceil__())
s("sum_list", lambda: sum([x, 1]))
s("sum_start", lambda: sum([x, x], 0.0))
s("has_radd", lambda: hasattr(float, "__radd__"))
s("has_abs_slot", lambda: hasattr(float, "__abs__"))
