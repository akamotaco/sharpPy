# p11: bytes / bytearray / slice protocol parity probes (CPython 3.12)
def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

b = b"Hello, World"
show("bytes.index", lambda: b[0])
show("bytes.neg", lambda: b[-1])
show("bytes.slice", lambda: b[1:4])
show("bytes.step", lambda: b[::-1])
show("bytes.oob", lambda: b[100])
show("bytes.add", lambda: b + b"!")
show("bytes.mul", lambda: b"ab" * 3)
show("bytes.contains", lambda: b"lo" in b)
show("bytes.int.contains", lambda: 104 in b)
show("bytes.upper", lambda: b.upper())
show("bytes.split", lambda: b.split(b","))
show("bytes.decode", lambda: b.decode())
show("bytes.decode.err", lambda: b"\xff".decode("utf-8"))
show("bytes.hex", lambda: b"\x00\x10".hex())
show("bytes.fromhex", lambda: bytes.fromhex("00ff7f"))
show("bytes.type", lambda: type(b).__name__)
show("bytes.bytearray.type", lambda: type(bytearray(b"xy")).__name__)

ba = bytearray(b"abc")
ba[0] = 65
show("ba.setitem", lambda: bytes(ba))
show("ba.append", lambda: (ba.append(33), bytes(ba))[1])
show("ba.oob.set", lambda: ba.__setitem__(99, 1))
show("ba.del", lambda: (ba.__delitem__(0), bytes(ba))[1])

show("bytes.iter", lambda: [c for c in b"abc"])
show("bytes.list", lambda: list(b"xy"))
show("bytes.cmp", lambda: b"a" < b"b")
show("bytes.cmp.ba", lambda: b"abc" == bytearray(b"abc"))
show("bytes.mod", lambda: b"%d" % 42)
show("bytes.fmt", lambda: f"{b'ab'!r}")
show("bytes.join", lambda: b",".join([b"a", b"b"]))
show("bytes.strip", lambda: b"  x  ".strip())
show("bytes.replace", lambda: b"aXa".replace(b"X", b"Y"))
show("bytes.subclass.idx", lambda: type("B", (bytes,), {})("hi")[0])

s = [0, 1, 2, 3, 4, 5]
show("list.slice", lambda: s[1:4])
show("list.slice.step", lambda: s[::-2])
show("list.slice.assign", lambda: (lambda t: (t.__setitem__(slice(1, 3), [9]), t)[1])(list(s)))
show("list.slice.assign.step", lambda: (lambda t: (t.__setitem__(slice(None, None, 2), [7, 7, 7]), t)[1])(list(s)))
show("list.slice.del", lambda: (lambda t: (t.__delitem__(slice(0, 2)), t)[1])(list(s)))
show("list.slice.del.step", lambda: (lambda t: (t.__delitem__(slice(None, None, 2)), t)[1])(list(s)))
show("list.slice.oob", lambda: (lambda t: (t.__setitem__(slice(2, 100), []), t)[1])(list(s)))

d = {1: "a", 2: "b"}
show("dict.slice.err", lambda: d[1:2])

class Seq:
    def __getitem__(self, i):
        if isinstance(i, slice):
            return ("slice", i.start, i.stop, i.step)
        return ("idx", i)

show("custom.slice", lambda: Seq()[1:2:3])
show("custom.idx", lambda: Seq()[5])

sl = slice(1, 2)
show("slice.attrs", lambda: (sl.start, sl.stop, sl.step))
show("slice.indices", lambda: sl.indices(5))
show("slice.indices.neg", lambda: slice(-3, None, -1).indices(6))
show("slice.repr", lambda: repr(slice(1, None, -2)))

t = (1, 2, 3, 4)
show("tuple.slice", lambda: t[1:])
show("tuple.slice.del.err", lambda: t.__delitem__(0))

show("str.slice.unicode", lambda: "한글abc"[1:3])
show("str.slice.assign.err", lambda: "ab".__setitem__(0, "c"))

m = memoryview(b"abc")
show("mv.type", lambda: type(m).__name__)
show("mv.tobytes", lambda: m.tobytes())
show("mv.itemsize", lambda: m.itemsize)

show("bytes.concat.err", lambda: b"a" + "b")
show("ba.concat", lambda: bytes(bytearray(b"a") + b"b"))
show("bytes.incompatible.cmp", lambda: b"a" < "a")
