# P0-23: builtin numeric subclasses resolve their arithmetic through the MRO.
# Measured against CPython 3.12.15. A subclass of int/float carries a value, so the base slot has to
# answer when the user declared nothing, and the user's own dunder has to win when they declared it.
# SharpPy sent both cases straight to the wrapped value, which lost the override.
# The asymmetry at the bottom is real and reproduced, not a compromise: operator.index() uses the
# value for an int subclass (long's C slot) while float() and int() honour the dunder.


class FUser(float):
    def __add__(self, other):
        return "user-add"

    def __radd__(self, other):
        return "user-radd"

    def __divmod__(self, other):
        return "user-divmod"

    def __floor__(self):
        return "user-floor"

    def __ceil__(self):
        return "user-ceil"

    def __trunc__(self):
        return "user-trunc"

    def __float__(self):
        return 9.5


class FPlain(float):
    pass


class IUser(int):
    def __add__(self, other):
        return "user-iadd"

    def __rsub__(self, other):
        return "user-isub"

    def __index__(self):
        return 3


class IPlain(int):
    pass


import math
import operator


def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__, str(exc)[:56])


fu = FUser(2.5)
fp = FPlain(2.5)
iu = IUser(5)
ip = IPlain(5)

# a declared dunder wins over the inherited base slot
s("f_add", lambda: fu + 2)
s("f_radd", lambda: 2 + fu)
s("f_divmod", lambda: divmod(fu, 2))
s("i_add", lambda: iu + 2)
s("i_rsub", lambda: 2 - iu)
s("math_floor", lambda: math.floor(fu))
s("math_ceil", lambda: math.ceil(fu))
s("math_trunc", lambda: math.trunc(fu))
s("float_of", lambda: float(fu))

# nothing declared, so the base slot answers with the value and the result keeps the base type
s("plain_f_add", lambda: fp + 1.0)
s("plain_f_add_type", lambda: type(fp + 1.0).__name__)
s("plain_i_add", lambda: ip + 1)
s("plain_i_add_type", lambda: type(ip + 1).__name__)
s("plain_f_divmod", lambda: divmod(fp, 2))
s("plain_f_floordiv", lambda: fp // 2)
s("plain_f_mod", lambda: fp % 2)
s("plain_f_pow", lambda: fp ** 2)
s("plain_f_floor", lambda: math.floor(fp))
s("plain_f_ceil", lambda: math.ceil(fp))
s("plain_i_floor", lambda: math.floor(ip))
s("plain_i_index", lambda: operator.index(ip))
s("plain_f_float", lambda: float(fp))
s("plain_f_int", lambda: int(fp))

# reflected divmod with the subclass on the right: int's divmod delegates to float arithmetic
s("rdivmod_fsub", lambda: divmod(2, fp))
s("rdivmod_i", lambda: divmod(2, ip))
s("rdivmod_mixed", lambda: divmod(7, 2.5))

# the asymmetry: index reads the value for an int subclass, float()/int() read the dunder
s("i_index_is_value", lambda: operator.index(iu))
s("i_index_attr", lambda: iu.__index__())
s("bin_is_value", lambda: bin(iu))
s("int_of_user_float_sub", lambda: int(type("G", (float,), {"__int__": lambda self: 77})(2.7)))
s("int_of_plain", lambda: int(fp))
s("float_of_user_int_sub", lambda: float(type("H", (float,), {"__float__": lambda self: 1.25})(2.7)))
