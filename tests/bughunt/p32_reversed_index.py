# p32: reversed(bytes/bytearray) + tuple.index/str find family arg validation.
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# reversed() sequence-protocol sources.
show("rev-bytes", lambda: list(reversed(b"abc")))
show("rev-bytearray", lambda: list(reversed(bytearray(b"ab"))))
show("rev-bytes-type", lambda: type(reversed(b"abc")).__name__)
show("rev-bytes-len", lambda: len(reversed(b"abc")))
show("rev-empty-bytes", lambda: list(reversed(b"")))
show("rev-str", lambda: list(reversed("abc")))
show("rev-tuple", lambda: list(reversed((1, 2, 3))))

# tuple.index: __index__ start/stop, bool, clamping, kwargs rejection.
show("tuple-index-bad", lambda: (1, 2).index(1, "x"))
show("tuple-index-bool", lambda: (1, 2).index(2, True))
show("tuple-index-stop", lambda: (1, 2, 3, 1).index(1, 1, 3))
show("tuple-index-neg", lambda: (1, 2, 3, 4).index(3, -3))
show("tuple-index-kw", lambda: (1, 2).index(1, start=0))
show("tuple-index-absent", lambda: (1, 2).index(5))

# str find family: non-integer start/stop raise, None keeps default, bool works.
for m in ("index", "find", "rindex", "rfind", "count", "startswith", "endswith"):
    show(f"{m}-badstart", lambda m=m: getattr("xabcy", m)("c", "x")
         if m not in ("startswith", "endswith")
         else getattr("xabcy", m)("c", "x"))
    show(f"{m}-bool", lambda m=m: getattr("xabcy", m)("c", True)
         if m not in ("startswith", "endswith")
         else getattr("xabcy", m)("b", True))
show("str-index-none", lambda: "xabcy".index("c", None, None))
show("str-index-kw", lambda: "abc".index("a", start=0))
show("str-find-start", lambda: "xabcy".find("c", 4))
show("str-count-start", lambda: "aaa".count("a", True))
