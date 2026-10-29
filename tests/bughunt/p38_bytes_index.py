# p38: bytes find/count/index/rfind/rindex validate start/stop via __index__.
def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


b = b"hello world"
s("find-badstart", lambda: b.find(b"o", "x"))
s("find-None", lambda: b.find(b"o", None))
s("find-True", lambda: b.find(b"o", True))
s("find-ok", lambda: b.find(b"o", 5))
s("find-startstop", lambda: b.find(b"o", 1, 4))
s("count-badstart", lambda: b.count(b"o", "x"))
s("count-startstop", lambda: b.count(b"o", 1, 4))
s("index-badstart", lambda: b.index(b"o", "x"))
s("index-neg", lambda: b.index(b"r", -5))
s("rfind-badstart", lambda: b.rfind(b"o", "x"))
s("rfind-ok", lambda: b.rfind(b"o"))
s("rindex-badstart", lambda: b.rindex(b"o", "x"))


class I:
    def __index__(self):
        return 5


s("find-index-obj", lambda: b.find(b"o", I()))
s("count-index-obj", lambda: b.count(b"l", I(), 10))
