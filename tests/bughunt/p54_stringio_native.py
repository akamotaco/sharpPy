# io B2: _io.StringIO is the native C# type (core/PyStringIO.cs / Modules/_io/stringio.c).
# The 93 python lines that used to stand in for it are gone from Lib/io.py, and the type now
# carries the real chain StringIO -> _TextIOBase -> _IOBase -> object.
# Measured against CPython 3.12.15.


import io


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:62])


s("mro", lambda: [c.__name__ for c in io.StringIO.__mro__])
s("iobase_mro", lambda: [c.__name__ for c in io.IOBase.__mro__])
s("module", lambda: io.StringIO.__module__)
s("isinstance_text", lambda: isinstance(io.StringIO("a"), io.TextIOBase))
s("isinstance_base", lambda: isinstance(io.StringIO("a"), io.IOBase))
s("abstract_iobase", lambda: type(io.IOBase()).__name__)
s("abstract_text", lambda: type(io.TextIOBase()).__name__)
s("repr_shape", lambda: type(repr(io.StringIO())).__name__)
b = io.StringIO("abc")
s("read_all", lambda: b.read())
s("read_after", lambda: b.read())
s("tell_end", lambda: b.tell())
s("seek0_read2", lambda: (b.seek(0), b.read(2)))
s("read_none", lambda: b.read(None))
s("read_neg", lambda: (b.seek(0), b.read(-1)))
s("iter_is_self", lambda: (lambda t: iter(t) is t)(b))
w = io.StringIO()
s("write_len", lambda: w.write("xy"))
s("write_ret_type", lambda: type(w.write("z")).__name__)
s("getvalue", lambda: w.getvalue())
s("tell_after", lambda: w.tell())
s("write_int", lambda: w.write(5))
s("readlines", lambda: io.StringIO("a\nb\nc").readlines())
s("readlines_hint", lambda: io.StringIO("a\nb\nc").readlines(2))
s("readline", lambda: io.StringIO("a\nb").readline())
s("readline_empty", lambda: io.StringIO("").readline())
s("iter_lines", lambda: [x for x in io.StringIO("a\nb\n")])
s("writelines", lambda: (lambda t: (t.writelines(["p\n", "q"]), t.getvalue()))(io.StringIO()))
s("newline_none", lambda: io.StringIO("a\r\nb\rc", newline=None).read())
s("newline_lf", lambda: io.StringIO("a\r\nb\rc", newline="\n").read())
s("newline_cr", lambda: io.StringIO("a\r\nb\rc", newline="\r").read())
s("write_trans_crlf", lambda: (lambda t: (t.write("a\nb"), t.getvalue()))(
    io.StringIO(newline="\r\n")))
s("write_trans_none", lambda: (lambda t: (t.write("a\nb"), t.getvalue()))(
    io.StringIO(newline=None)))
s("kw_names", lambda: io.StringIO(initial="z", newline="\n").getvalue())
s("bad_newline", lambda: io.StringIO("x", newline="q"))
s("bytes_arg", lambda: io.StringIO(b"x"))
s("truncate_all", lambda: (lambda t: (t.truncate(), t.getvalue()))(io.StringIO("abcd")))
s("truncate_n", lambda: (lambda t: (t.truncate(2), t.getvalue()))(io.StringIO("abcd")))
s("truncate_pos", lambda: (lambda t: (t.seek(1), t.truncate(), t.getvalue()))(io.StringIO("abcd")))
s("truncate_neg", lambda: (lambda t: t.truncate(-1))(io.StringIO("abcd")))
s("seek_end", lambda: (lambda t: (t.seek(0, 2), t.tell()))(io.StringIO("abcd")))
s("seek_rel", lambda: (lambda t: (t.seek(1), t.seek(1, 1), t.tell()))(io.StringIO("abcd")))
s("seek_bad", lambda: (lambda t: t.seek(0, 9))(io.StringIO("abcd")))
s("attrs", lambda: (io.StringIO("a").readable(), io.StringIO("a").writable(),
                    io.StringIO("a").seekable(), io.StringIO("a").isatty()))
s("flush", lambda: io.StringIO("a").flush())
s("fileno", lambda: io.StringIO("a").fileno())
s("newlines_attr", lambda: io.StringIO("a\nb\rc").newlines)
s("newlines_read", lambda: (lambda t: (t.read(), t.newlines))(io.StringIO("a\nb\rc")))
s("line_buffering", lambda: io.StringIO("a").line_buffering)
s("closed_false", lambda: io.StringIO("a").closed)
s("closed_true", lambda: (lambda t: (t.close(), t.closed))(io.StringIO("a")))
s("read_closed", lambda: (lambda t: (t.close(), t.read()))(io.StringIO("a")))
s("getvalue_closed", lambda: (lambda t: (t.close(), t.getvalue()))(io.StringIO("a")))
s("write_closed", lambda: (lambda t: (t.close(), t.write("z")))(io.StringIO("a")))
s("close_twice", lambda: (lambda t: (t.close(), t.close(), t.closed))(io.StringIO("a")))
s("with_stmt", lambda: (lambda: [t.closed for t in [io.StringIO("a")]][:0] or
                        [x for x in [(lambda f: (f.closed, f.getvalue()))(
                            (lambda ctx: (ctx.__enter__(), ctx.__exit__(None, None, None)))(
                                io.StringIO("hi")) or io.StringIO())]])())
s("big_write", lambda: (lambda t: (t.write("x" * 100000), len(t.getvalue())))(io.StringIO()))
