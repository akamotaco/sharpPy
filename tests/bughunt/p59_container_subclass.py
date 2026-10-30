# Builtin container subclasses: the declared dunder vs the inherited base slot.
# Measured against CPython 3.12.15. CPython's sq_length / sq_concat / tp_richcompare for a heap
# subclass are the slot wrapper around the dunder the class publishes, so a user declaration wins;
# when the class declares nothing, the base slot reads the value.
# The remaining divergences in this family are listed in docs/known_gaps.md as P0-24; this probe
# pins the behaviour that now matches, including the cases that were outright crashes.


class SUser(str):
    def __len__(self):
        return 99

    def __iter__(self):
        return iter(["u", "s", "e", "r"])

    def __contains__(self, x):
        return "user-contains"

    def __add__(self, other):
        return "user-str-add"

    def __eq__(self, other):
        return "user-str-eq"

    def __hash__(self):
        return 7


class SPlain(str):
    pass


class DUser(dict):
    def __len__(self):
        return 99

    def __contains__(self, k):
        return "user-dcontains"


class DPlain(dict):
    pass


class LUser(list):
    def __len__(self):
        return 99

    def __contains__(self, x):
        return False


class LPlain(list):
    pass


class IUser(int):
    def __iter__(self):
        return iter([3, 4])


class IPlain(int):
    pass


class FUser(float):
    def __iter__(self):
        return iter([9])


class FPlain(float):
    pass


class TUser(tuple):
    def __len__(self):
        return 99

    def __iter__(self):
        return iter(["t"])

    def __add__(self, other):
        return "user-tadd"


class TPlain(tuple):
    pass


def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__, str(exc)[:60])


su = SUser("abc")
sp = SPlain("abc")
du = DUser({"a": 1})
dp = DPlain({"a": 1})
lu = LUser([1, 2])
lp = LPlain([1, 2])

# a declared dunder wins over the inherited base slot
s("s_len", lambda: len(su))
s("s_add", lambda: su + "z")
s("s_eq", lambda: su == "z")
s("s_ne", lambda: su != "z")
s("s_hash", lambda: hash(su))
# `x in s` is always a bool, so a truthy __contains__ result reads as True
s("s_in_declared_true", lambda: "z" in su)
s("s_in_type", lambda: type("z" in su).__name__)
s("d_len", lambda: len(du))
s("d_in_declared", lambda: "zz" in du)
s("l_len", lambda: len(lu))
s("l_in_declared_false", lambda: 1 in lu)

# iteration: the declared __iter__ wins on every value-carrying wrapper, and the not-iterable
# error names the subclass rather than the base type
s("s_iter", lambda: list(su))
s("s_iter_for", lambda: [c for c in su])
s("s_iter_type", lambda: type(iter(su)).__name__)
s("i_iter", lambda: list(IUser(5)))
s("i_iter_type", lambda: type(iter(IUser(5))).__name__)
s("f_iter", lambda: list(FUser(1.5)))
s("i_plain_iter", lambda: iter(IPlain(5)))
s("f_plain_iter", lambda: iter(FPlain(1.5)))
s("s_plain_iter", lambda: list(SPlain("xy")))

# nothing declared: the base slot reads the value, and the value protocol keeps working
s("plain_s_len", lambda: len(sp))
s("plain_s_add", lambda: sp + "z")
s("plain_s_mul", lambda: sp * 2)
s("plain_s_mod", lambda: "%s!" % sp)
s("plain_s_iter", lambda: list(sp))
s("plain_s_getitem", lambda: sp[1])
s("plain_s_slice", lambda: sp[0:2])
s("plain_s_upper", lambda: sp.upper())
s("plain_s_contains", lambda: "b" in sp)
s("plain_s_eq", lambda: sp == "abc")
s("plain_d_len", lambda: len(dp))
s("plain_d_iter", lambda: list(dp))
s("plain_d_getitem", lambda: dp["a"])
s("plain_d_keys", lambda: sorted(dp.keys()))
s("plain_l_len", lambda: len(lp))
s("plain_l_in", lambda: 1 in lp)
s("plain_l_append", lambda: (lp.append(3), len(lp))[1])

# a plain str subclass still behaves like a str where no dunder is involved
s("sub_no_dunder_len", lambda: len(type("Z", (str,), {})("abcd")))
s("sub_with_attr", lambda: (lambda o: (o.n, len(o)))(type("Z", (str,), {"n": 3})("ab")))
s("subclass_of_subclass", lambda: len(type("Y", (SUser,), {})("ab")))
s("subclass_of_subclass_own", lambda: len(type("Y", (SUser,), {"__len__": lambda self: 4})("ab")))

# tuple subclass: the declared dunder wins, and the plain tuple keeps its value protocol
s("t_len", lambda: len(TUser((1, 2))))
s("t_iter", lambda: list(TUser((1, 2))))
s("t_for", lambda: [x for x in TUser((1, 2))])
s("t_add", lambda: TUser((1, 2)) + (3,))
s("t_plain_len", lambda: len(TPlain((1, 2))))
s("t_plain_iter", lambda: list(TPlain((1, 2))))
s("t_plain_add", lambda: TPlain((1, 2)) + (3,))
s("t_plain_mul", lambda: TPlain((1,)) * 3)
s("t_plain_getitem", lambda: TPlain((1, 2))[1])
s("t_plain_slice", lambda: TPlain((1, 2, 3))[0:2])
s("t_plain_count", lambda: TPlain((1, 1, 2)).count(1))
s("t_plain_index", lambda: TPlain((1, 2)).index(2))
s("t_plain_contains", lambda: 2 in TPlain((1, 2)))
s("t_unpack", lambda: [x for x in TPlain((1, 2))][1])
a, b = TPlain((7, 8))
print("t_star_unpack", a, b)
s("t_nested_cmp", lambda: TPlain((1, 2)) == (1, 2))
s("t_hash", lambda: hash(TPlain((1, 2))) == hash((1, 2)))
s("t_dict_key", lambda: {(TPlain((1, 2))): "v"}[(1, 2)])

# __len__ must produce an int, as CPython converts it through __index__
s("bad_len", lambda: len(type("Bad", (object,), {"__len__": lambda self: "short"})()))
