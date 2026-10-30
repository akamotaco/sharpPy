# p68: the numeric attribute surface - bool is an int, and copies are not receivers (P0-30).
#
# CPython's int/float/complex publish real, imag, numerator, denominator, conjugate and
# __getnewargs__ in the type dict. Three rules came out of reading those tables and they were all
# broken here:
#
#   1. the self check is PyLong_Check, and bool passes it - (True).real is 1, not a TypeError. The CLR
#      does not model bool as a PyInt, so every check site has to say so.
#   2. the value returned is long_long / float_getreal, not the receiver. long_long keeps identity for
#      an exact int but copies a bool or an int subclass, which is why type(I(3).real) is int;
#      float_getreal always builds a fresh PyFloat, so type(F(2.5).real) is float.
#   3. assignment consults the MERGED type dict (_PyType_Lookup). The setter used to look only at user
#      class frames, so a getset in float's TypeDict was invisible: setattr(2.5, "real", 1) answered
#      "has no attribute" where CPython reports the getset's own "not writable".
#
# The unbound error text is the one descr_check form used by every builtin method and getset:
# "descriptor '<name>' for '<type>' objects doesn't apply to a '<self>' object".

class I(int):
    pass


class F(float):
    pass


def s(l, f):
    try:
        v = f()
        print(l, ascii(v), type(v).__name__)
    except Exception as exc:
        print(l, "EXC", type(exc).__name__ + ":", ascii(str(exc))[:62])


print("--- bool is an int for these slots ---")
s("true_real", lambda: (True).real)
s("true_imag", lambda: (True).imag)
s("true_num", lambda: (True).numerator)
s("true_den", lambda: (True).denominator)
s("true_conj", lambda: (True).conjugate())
s("true_getnewargs", lambda: True.__getnewargs__())
s("false_real", lambda: (False).real)

print("--- the value is a copy, not the receiver ---")
s("big_real", lambda: (10 ** 30).real)
s("isub_real_type", lambda: type(I(3).real).__name__)
s("isub_real_value", lambda: int(I(3).real))
s("isub_num_type", lambda: type(I(3).numerator).__name__)
s("isub_conj_type", lambda: type(I(3).conjugate()).__name__)
s("isub_getnewargs", lambda: (I(3).__getnewargs__(), type(I(3).__getnewargs__()[0]).__name__))
s("isub_trunc_type", lambda: type(I(3).__trunc__()).__name__)
s("f_sub_real_type", lambda: type(F(2.5).real).__name__)
s("f_sub_conj_type", lambda: type(F(2.5).conjugate()).__name__)

print("--- __getnewargs__ (the pickle surface) ---")
s("int_getnewargs", lambda: (5).__getnewargs__())
s("float_getnewargs", lambda: (2.5).__getnewargs__())
s("float_sub_getnewargs", lambda: F(2.5).__getnewargs__())
s("complex_getnewargs", lambda: (1 + 2j).__getnewargs__())
s("getnewargs_extra_arg", lambda: (5).__getnewargs__(1))

print("--- assignment consults the native type dict ---")
s("set_float_real", lambda: setattr(2.5, "real", 1))
s("set_int_real", lambda: setattr(5, "real", 1))
s("del_float_real", lambda: delattr(2.5, "real"))
s("set_list_real", lambda: setattr([], "real", 1))
s("set_str_doc", lambda: setattr("s", "__doc__", "x"))
s("set_float_unknown", lambda: setattr(2.5, "zz", 1))
s("set_int_name_int", lambda: setattr(5, 2, 1))

# The two spellings below are left out on purpose: CPython's descriptor objects carry __get__/
# __set__/__delete__ in their own type dict, and SharpPy's do not (nor does its getset descriptor
# report the type name "getset_descriptor" - it reports "property"). Registered in docs/known_gaps.md;
# including it here would pin a gap instead of a behaviour.
#   int.__dict__["real"].__get__(5) -> 5 in CPython

print("--- unbound self checks and the published surface ---")
s("int_conjugate_wrong_self", lambda: int.conjugate("a"))
s("int_conjugate_extra", lambda: (5).conjugate(1))
s("float_conjugate_extra", lambda: (2.5).conjugate(1))
s("complex_conjugate_extra", lambda: (1 + 2j).conjugate(1))
s("int_bit_length_extra", lambda: (5).bit_length(1))
s("complex_wrong_self", lambda: complex.conjugate("a"))
s("complex_real", lambda: (1 + 2j).real)
s("complex_imag", lambda: (1 + 2j).imag)
s("complex_conj", lambda: (1 + 2j).conjugate())
s("hasattr_complex_real", lambda: hasattr(complex, "real"))
s("hasattr_complex_conjugate", lambda: hasattr(complex, "conjugate"))
s("hasattr_int_real", lambda: hasattr(int, "real"))
s("hasattr_float_getnewargs", lambda: hasattr(float, "__getnewargs__"))
s("complex_conj_still_works_on_instance", lambda: (1 - 2j).conjugate() == 1 + 2j)
print("p68 done")
