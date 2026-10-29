# io B3: _io.BytesIO is the native C# type (core/PyBytesIO.cs / Modules/_io/bytesio.c).
# Lib/io.py no longer re-implements it. Note the deliberate asymmetries against StringIO, all
# measured and reproduced: lowercase "negative size value"/"negative seek value"/"invalid whence",
# the closed message WITH a trailing period, an accepted initial_bytes keyword, and cur-relative
# seeks that work. Recorded deviation: getbuffer() is a snapshot, so it does not track later
# writes the way CPython's live view does.


import io


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:60])


s("mro", lambda: [c.__name__ for c in io.BytesIO.__mro__])
s("module", lambda: io.BytesIO.__module__)
b = io.BytesIO(b"abc")
s("getvalue", lambda: b.getvalue())
s("read_all", lambda: b.read())
s("read_after", lambda: b.read())
s("tell", lambda: b.tell())
s("seek0_read2", lambda: (b.seek(0), b.read(2)))
s("read_none", lambda: b.read(None))
s("read_0", lambda: (b.seek(0), b.read(0)))
s("iter_is_self", lambda: (lambda t: iter(t) is t)(io.BytesIO(b"a\nb")))
s("iter_lines", lambda: [x for x in io.BytesIO(b"a\nb\n")])
s("readline", lambda: io.BytesIO(b"a\nb").readline())
s("readline_empty", lambda: io.BytesIO(b"").readline())
s("readlines", lambda: io.BytesIO(b"a\nb\nc").readlines())
w = io.BytesIO()
s("write_ret", lambda: w.write(b"xy"))
s("getvalue_w", lambda: w.getvalue())
s("write_str", lambda: w.write("z"))
s("write_ba", lambda: w.write(bytearray(b"q")))
s("readinto", lambda: (lambda t: (lambda buf: (t.readinto(buf), bytes(buf)))(
    bytearray(2)))(io.BytesIO(b"abcdef")))
s("readinto_short", lambda: (lambda t: (lambda buf: (t.readinto(buf), bytes(buf)))(
    bytearray(9)))(io.BytesIO(b"ab")))
s("init_str", lambda: io.BytesIO("x").getvalue())
s("init_int", lambda: io.BytesIO(5).getvalue())
s("init_none", lambda: io.BytesIO(None).getvalue())
s("init_bytearray", lambda: io.BytesIO(bytearray(b"hey")).getvalue())
s("kw_initial", lambda: io.BytesIO(initial_bytes=b"x").getvalue())
s("bad_kw", lambda: io.BytesIO(b"x", zz=1))
s("truncate", lambda: (lambda t: (t.truncate(), t.getvalue()))(io.BytesIO(b"abcd")))
s("truncate_n", lambda: (lambda t: (t.truncate(2), t.getvalue()))(io.BytesIO(b"abcd")))
s("truncate_neg", lambda: io.BytesIO(b"abcd").truncate(-1))
s("seek_end", lambda: (lambda t: (t.seek(0, 2), t.tell()))(io.BytesIO(b"abcd")))
s("seek_neg", lambda: (lambda t: t.seek(-5))(io.BytesIO(b"abcd")))
s("seek_oo", lambda: (lambda t: t.seek(0, 9))(io.BytesIO(b"abcd")))
s("attrs", lambda: (io.BytesIO(b"a").readable(), io.BytesIO(b"a").writable(),
                    io.BytesIO(b"a").seekable(), io.BytesIO(b"a").isatty()))
s("flush", lambda: io.BytesIO(b"a").flush())
s("fileno", lambda: io.BytesIO(b"a").fileno())
s("closed_false", lambda: io.BytesIO(b"a").closed)
s("closed_true", lambda: (lambda t: (t.close(), t.closed))(io.BytesIO(b"a")))
s("read_closed", lambda: (lambda t: (t.close(), t.read()))(io.BytesIO(b"a")))
s("getvalue_closed", lambda: (lambda t: (t.close(), t.getvalue()))(io.BytesIO(b"a")))
s("close_twice", lambda: (lambda t: (t.close(), t.close(), t.closed))(io.BytesIO(b"a")))
s("getbuffer", lambda: bytes(io.BytesIO(b"ab").getbuffer()))
s("isinstance_buf", lambda: isinstance(io.BytesIO(b"a"), io.BufferedIOBase))
s("isinstance_raw", lambda: isinstance(io.BytesIO(b"a"), io.RawIOBase))
s("detach", lambda: io.BytesIO(b"a").detach())
s("raw", lambda: io.BytesIO(b"a").raw)
s("peek", lambda: io.BytesIO(b"abc").peek(2))
s("read1", lambda: io.BytesIO(b"abc").read1(2))
s("getstate", lambda: io.BytesIO(b"ab").__getstate__())
s("setstate", lambda: (lambda t: (t.__setstate__((b"zz", 1, False)), t.getvalue(), t.tell()))(
    io.BytesIO()))
