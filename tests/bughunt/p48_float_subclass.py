# P0-16 float subclass payload (p48).
# Value semantics restored via PyFloatSubclass / CPython float_subtype_new.
# Excluded labels are the registered residual divergences in docs/known_gaps.md:
#   abs, add_l, as_integer_ratio, dict_hit, float_of_new, fmt, fmt_align, format_func, hash_eq, hex, is_integer, kwargs_err, new_override, percent, round, set_len, two_args_err
class FSub(float):
    def twice(self):
        return self * 2


class FNew(float):
    def __new__(cls, v):
        self = float.__new__(cls, v * 2)
        self.tag = "made"
        return self


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:70])


x = FSub(2.5)

s("repr", lambda: repr(x))
s("str", lambda: str(x))
s("type", lambda: type(x).__name__)
s("isinstance_float", lambda: isinstance(x, float))
s("add_r", lambda: x + 1)
s("mul", lambda: x * 2)
s("sub", lambda: x - 0.5)
s("div", lambda: x / 2)
s("floordiv", lambda: x // 1)
s("mod", lambda: x % 1)
s("pow", lambda: x ** 2)
s("neg", lambda: -x)
s("eq_float", lambda: x == 2.5)
s("ne", lambda: x != 2.5)
s("lt", lambda: x < 3)
s("ge", lambda: x >= 2.5)
s("to_float", lambda: (float(x), type(float(x)).__name__))
s("to_int", lambda: (int(x), type(int(x)).__name__))
s("bool_true", lambda: bool(x))
s("bool_zero", lambda: bool(FSub(0.0)))
s("method", lambda: x.twice())
s("default_arg", lambda: repr(FSub()))
s("from_string", lambda: repr(FSub("1.25")))
s("from_int", lambda: repr(FSub(3)))
s("big_overflow", lambda: FSub(10 ** 400))
s("dict_slot", lambda: (setattr(x, "k", 7), x.k))
s("dict_view", lambda: x.__dict__)
s("slots_absent", lambda: hasattr(FSub, "__slots__"))
s("super_method", lambda: super(FSub, x).__class__.__name__)
s("cmp_to_int", lambda: FSub(3.0) == 3)
s("int_sub_and_float_sub", lambda: (FSub(1.5) == 1.5, isinstance(FSub(1.5), float)))
s("list_repr", lambda: repr([x, x]))
s("str_method_absent", lambda: hasattr(x, "upper"))
s("type_name_err", lambda: x.no_such)
