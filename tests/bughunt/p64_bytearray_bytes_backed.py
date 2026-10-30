# p64: bytearray's bytes-backed method surface (P0-14).
#
# CPython's bytearray carries the whole bytes method surface and its non-mutating methods return
# bytearray, including bytearray elements inside split/rsplit/splitlines/partition/rpartition results.
# SharpPy had the search/query family from p40 and none of the rest, so dir(bytearray) lacked upper,
# strip, split, replace, hex, join and the is* family, and bytearray(b"ab").upper() was an
# AttributeError.
#
# The fix delegates: a bytearray method calls the descriptor bytes already registers against a proxy
# holding the same bytes, and converts the result type back. So this probe pins three things at once:
#   * the value and the result type of every delegated method,
#   * the class-level helpers fromhex/maketrans, whose result types differ on purpose
#     (fromhex -> bytearray, maketrans -> bytes),
#   * the error message families, which differ per method in CPython and are reproduced from
#     measurement rather than from a rule.

def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", str(exc)[:70])


ba = bytearray(b"Hello World")
low = bytearray(b"aB1 cD2")
num = bytearray(b"1234")
sp = bytearray(b"  xy  ")
mix = bytearray(b"a\tb\t\tc")


s("type_upper", lambda: type(ba.upper()).__name__)
s("upper", lambda: ba.upper())
s("lower", lambda: ba.lower())
s("capitalize", lambda: ba.capitalize())
s("swapcase", lambda: ba.swapcase())
s("title", lambda: bytearray(b"ab cd").title())
s("strip", lambda: sp.strip())
s("lstrip", lambda: sp.lstrip())
s("rstrip", lambda: sp.rstrip())
s("strip_chars", lambda: bytearray(b"xxaxx").strip(b"x"))
s("zfill", lambda: bytearray(b"42").zfill(5))
s("ljust", lambda: bytearray(b"ab").ljust(5, b"-"))
s("rjust", lambda: bytearray(b"ab").rjust(5))
s("center", lambda: bytearray(b"ab").center(6, b"*"))
s("expandtabs", lambda: mix.expandtabs())
s("expandtabs_4", lambda: mix.expandtabs(4))
s("hex", lambda: bytearray(b"\x01\xab").hex())
s("hex_sep", lambda: bytearray(b"\x01\xab").hex(":"))
s("fromhex", lambda: bytearray.fromhex("01ab"))
s("isalnum", lambda: ba.isalnum())
s("isalpha", lambda: bytearray(b"abc").isalpha())
s("isascii", lambda: bytearray(b"abc\xff").isascii())
s("isdigit", lambda: num.isdigit())
s("islower", lambda: bytearray(b"abc").islower())
s("isspace", lambda: bytearray(b" \t").isspace())
s("istitle", lambda: ba.istitle())
s("isupper", lambda: bytearray(b"ABC").isupper())
s("split_default", lambda: ba.split())
s("split_sep", lambda: bytearray(b"a,b,c").split(b","))
s("split_maxsplit", lambda: bytearray(b"a,b,c").split(b",", 1))
s("split_kw", lambda: bytearray(b"a,b").split(sep=b","))
s("rsplit", lambda: bytearray(b"a,b,c").rsplit(b","))
s("splitlines", lambda: bytearray(b"a\nb\r\nc").splitlines())
s("splitlines_keepends", lambda: bytearray(b"a\nb").splitlines(True))
s("partition", lambda: ba.partition(b"o W"))
s("partition_elem_type", lambda: type(ba.partition(b"o W")[0]).__name__)
s("rpartition", lambda: ba.rpartition(b"o"))
s("join", lambda: bytearray(b",").join([b"a", bytearray(b"b")]))
s("join_type", lambda: type(bytearray(b",").join([b"a"])).__name__)
s("replace", lambda: ba.replace(b"o", b"0"))
s("replace_count", lambda: ba.replace(b"o", b"0", 1))
s("maketrans", lambda: bytes.maketrans(b"ab", b"cd") == bytearray.maketrans(b"ab", b"cd"))
s("decode", lambda: bytearray(b"ab").decode())
s("decode_kw", lambda: bytearray(b"ab").decode(encoding="utf-8"))
s("bytes_dunder", lambda: bytes(bytearray(b"ab")))
s("getnewargs", lambda: bytearray(b"ab").__getnewargs__())
s("add", lambda: bytearray(b"a") + b"b")
s("add_type", lambda: type(bytearray(b"a") + b"b").__name__)
s("mul", lambda: bytearray(b"ab") * 2)
s("rmul", lambda: 2 * bytearray(b"ab"))
s("iter", lambda: list(iter(bytearray(b"ab"))))
s("upper_arity", lambda: ba.upper(1))
s("split_arity", lambda: ba.split(b" ", 1, 2))
s("bad_self", lambda: bytes.upper(bytearray(b"a")))
s("bad_self_ba", lambda: bytearray.upper(b"a"))
s("bad_kw", lambda: ba.upper(x=1))
s("mk_type", lambda: type(bytearray.maketrans(b"ab", b"cd")).__name__)
s("mk3", lambda: bytearray.maketrans(b"abc"))
s("rsplit_kw", lambda: bytearray(b"a,b,c").rsplit(b",", maxsplit=1))
s("expandtabs_kw", lambda: bytearray(b"a\tb").expandtabs(tabsize=2))
s("str_sep", lambda: bytearray(b"a,b").split(","))
s("hex_bad_len", lambda: bytearray(b"ab").hex("ab"))
s("hex_bad_type", lambda: bytearray(b"ab").hex(1))
s("hex_3args", lambda: bytearray(b"ab").hex(":", 1, 2))
s("join_ba_items", lambda: bytearray(b",").join([b"a", bytearray(b"b"), memoryview(b"c")]))
s("join_str_item", lambda: bytearray(b",").join(["a"]))
s("join_tuple", lambda: bytearray(b",").join((b"a", b"b")))
s("join_int", lambda: bytearray(b",").join(5))
s("zfill_0", lambda: bytearray(b"7").zfill())

for label, fn in [
    ("bytes.fromhex", lambda: type(bytes.fromhex).__name__),
    ("bytearray.fromhex", lambda: type(bytearray.fromhex).__name__),
    ("bytes.maketrans", lambda: type(bytes.maketrans).__name__),
    ("bytearray.maketrans", lambda: type(bytearray.maketrans).__name__),
    ("fh_call", lambda: bytearray.fromhex("01ab")),
    ("fh_empty", lambda: bytearray.fromhex()),
    ("fh_type_arg", lambda: bytearray.fromhex(int)),
    ("fh_two", lambda: bytearray.fromhex("01", "ab")),
    ("ba_fh_odd", lambda: bytearray.fromhex("abc")),
    ("ba_fh_bad", lambda: bytearray.fromhex("0z")),
    ("mt", lambda: type(bytearray.maketrans(b"ab", b"cd")).__name__),
]:
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", str(exc)[:64])

s("b_fh_0", lambda: bytes.fromhex())
s("b_fh_2", lambda: bytes.fromhex("01", "ab"))
s("b_fh_kw", lambda: bytes.fromhex("01", x=1))
s("b_mt_1", lambda: bytes.maketrans(b"a"))
s("b_mt_kw", lambda: bytes.maketrans(b"a", b"b", x=1))
s("ba_fh_0", lambda: bytearray.fromhex())
s("ba_fh_2", lambda: bytearray.fromhex("01", "ab"))
s("ba_fh_kw", lambda: bytearray.fromhex("01", x=1))
s("ba_mt_kw", lambda: bytearray.maketrans(b"a", b"b", x=1))
