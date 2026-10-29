# p40: bytearray search/query family + empty-subsection counting semantics.
# Bytearray previously raised AttributeError for count/index/rfind/rindex/
# startswith/endswith/removeprefix/removesuffix, and bytes.count(b"") looped
# forever. (bytes-side int/tuple argument parity is tracked separately as P0-13.)
def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


ba = bytearray(b"hello world")
s("find", lambda: ba.find(b"w"))
s("find-int", lambda: ba.find(119))
s("find-badstart", lambda: ba.find(b"o", "x"))
s("find-None", lambda: ba.find(b"o", None))
s("find-True", lambda: ba.find(b"o", True))
s("find-range", lambda: ba.find(b"o", 1, 4))
s("find-negend", lambda: ba.find(b"o", 1, -3))
s("find-self", lambda: ba.find(bytearray(b"world")))
s("find-str-arg", lambda: ba.find("o"))
s("find-int-oob", lambda: ba.find(300))
s("rfind", lambda: ba.rfind(b"l"))
s("rfind-badstart", lambda: ba.rfind(b"l", "x"))
s("index", lambda: ba.index(b"r"))
s("index-miss", lambda: ba.index(b"zz"))
s("index-badstart", lambda: ba.index(b"o", "x"))
s("rindex", lambda: ba.rindex(b"l"))
s("rindex-miss", lambda: ba.rindex(b"zz"))
s("count", lambda: ba.count(b"l"))
s("count-int", lambda: ba.count(108))
s("count-range", lambda: ba.count(b"l", 3, 10))
s("count-overlap", lambda: bytearray(b"aaa").count(b"aa"))
s("count-oob", lambda: ba.count(300))
s("count-0args", lambda: ba.count())
s("count-badstart", lambda: ba.count(b"l", "x"))
s("startswith", lambda: ba.startswith(b"he"))
s("startswith-tuple", lambda: ba.startswith((b"he", b"x")))
s("startswith-tuple-bad", lambda: ba.startswith((b"a", "b")))
s("startswith-int", lambda: ba.startswith(104))
s("startswith-range", lambda: ba.startswith(b"l", 2, 4))
s("endswith", lambda: ba.endswith(b"ld"))
s("endswith-negstart", lambda: ba.endswith(b"ld", -2))
s("removeprefix", lambda: ba.removeprefix(b"hello "))
s("removeprefix-none", lambda: ba.removeprefix(b"zz"))
s("removesuffix", lambda: ba.removesuffix(b" world"))
s("removesuffix-2args", lambda: ba.removesuffix(b"a", b"b"))
s("immutable-after", lambda: bytes(ba))

# Empty subsection counts boundary positions across bytes / bytearray / str.
for pre, o in (("b", b"hello world"), ("ba", bytearray(b"hello world")), ("s", "hello world")):
    empty = b"" if pre != "s" else ""
    s(pre + ".count-empty", lambda o=o, empty=empty: o.count(empty))
    s(pre + ".count-empty-2-5", lambda o=o, empty=empty: o.count(empty, 2, 5))
    s(pre + ".count-empty-5-5", lambda o=o, empty=empty: o.count(empty, 5, 5))
    s(pre + ".count-empty-neg", lambda o=o, empty=empty: o.count(empty, -4, -1))
    s(pre + ".count-empty-oob", lambda o=o, empty=empty: o.count(empty, 9, 99))
    s(pre + ".count-empty-rev", lambda o=o, empty=empty: o.count(empty, 7, 3))
    s(pre + ".find-empty", lambda o=o, empty=empty: o.find(empty))
    s(pre + ".rfind-empty", lambda o=o, empty=empty: o.rfind(empty))

s("str.count-nonempty", lambda: "aaaa".count("aa"))
s("str.empty-count", lambda: "".count(""))
