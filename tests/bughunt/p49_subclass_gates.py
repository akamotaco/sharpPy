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
