class I(int):
    pass


class F(float):
    pass


class Q:
    def __round__(self):
        return 42


class R(float):
    def __round__(self):
        return "custom"


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:66])


s("i_round", lambda: round(I(5)))
s("i_round_type", lambda: type(round(I(5))).__name__)
s("i_round_nd", lambda: (round(I(5), 2), type(round(I(5), 2)).__name__))
s("i_round_neg", lambda: round(I(1234), -2))
s("i_round_none", lambda: round(I(5), None))
s("f_round", lambda: round(F(2.5)))
s("f_round_type", lambda: type(round(F(2.5))).__name__)
s("f_round_nd", lambda: (round(2.675, 2), type(round(2.675, 2)).__name__))
s("f_round_even", lambda: (round(2.5), round(3.5), round(-2.5)))
s("f_round_kw", lambda: round(F(2.5), ndigits=1))
s("bad_nd_int", lambda: round(5, "x"))
s("bad_nd_fl", lambda: round(1.5, "x"))
s("three_args", lambda: round(5, 1, 2))
s("huge_nd_int", lambda: round(5, 10 ** 30))
s("huge_nd_fl", lambda: round(1.5, 10 ** 30))
s("index_nd", lambda: round(2.675, type("Q2", (), {"__index__": lambda z: 2})()))
s("user_round", lambda: round(Q()))
s("user_override_fl", lambda: round(R(1.0)))
s("hasattr_int_round", lambda: hasattr(int, "__round__"))
s("hasattr_fl_round", lambda: hasattr(float, "__round__"))
s("hasattr_fl_hash", lambda: hasattr(float, "__hash__"))
s("round_repr", lambda: type(float.__round__).__name__)
s("hash_repr", lambda: repr(float.__hash__))
s("f_hash_eq", lambda: hash(F(2.5)) == hash(2.5))
s("f_hash_int", lambda: hash(F(2.0)) == hash(2))
s("f_dict", lambda: {2.5: "v"}[F(2.5)])
s("f_set", lambda: len({F(2.5), 2.5}))
s("i_hash_still", lambda: hash(I(5)) == hash(5))
s("unhashable_list", lambda: hash([1]))
s("round_noargs", lambda: round())
s("round_slot_kw", lambda: int.__round__(5, ndigits=2))
s("round_slot_arity", lambda: float.__round__(1.5, 1, 2))
