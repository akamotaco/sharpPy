# P0-18 builtin-subclass gates (p49 increment 1: int descriptors/getsets).
# CPython PyLong_Check accepts subclass instances; SharpPy used exact CLR type tests.
# Excluded (still divergent, see docs/known_gaps.md):
#   bytes_format, int_radd, int_round, list_repr
class I(int):
    pass


class S(str):
    pass


class Bs(bytes):
    pass


class L(list):
    pass


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:58])


        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:58])


s("int_format", lambda: format(I(5), "05d"))
s("int_bitlen", lambda: I(8).bit_length())
s("int_hash", lambda: hash(I(5)) == hash(5))
s("int_add", lambda: I(5) + 1)
s("str_upper", lambda: S("ab").upper())
s("str_format", lambda: format(S("a"), ">3"))
s("str_count", lambda: S("aaa").count("a"))
s("str_hash", lambda: hash(S("a")) == hash("a"))
s("bytes_decode", lambda: Bs(b"ab").decode())
s("bytes_upper", lambda: Bs(b"ab").upper())
s("bytes_hash", lambda: hash(Bs(b"ab")) == hash(b"ab"))
s("list_len", lambda: len(L([1, 2])))
s("list_reversed", lambda: list(reversed(L([1, 2]))))
s("list_hash_none", lambda: L.__hash__ is None)

# --- increment 3: reflected int slots read self's value (p49) ---
# CPython's nb_reflected slots read the digits of self, so a subclass contributes its
# value and the result is a plain int. SharpPy's intType reflected wrappers handed the
# subclass object to PyInt.Add, which returned NotImplemented.
s("i_radd_attr", lambda: hasattr(I(5), "__radd__"))
s("i_radd_call", lambda: I(5).__radd__(1))
s("i_rsub_call", lambda: I(5).__rsub__(1))
s("i_rdiv_call", lambda: I(5).__rtruediv__(1))
s("one_plus_i", lambda: 1 + I(5))
s("one_minus_i", lambda: 1 - I(5))
s("i_plus_i", lambda: I(5) + I(6))
s("i_eq_int", lambda: I(5) == 5)
s("int_abs_attr", lambda: hasattr(int, "__abs__"))
