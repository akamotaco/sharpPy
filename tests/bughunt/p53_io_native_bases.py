# P0-16/B1: the native io abstract bases exist in _io, per CPython Modules/_io/iobase.c.
# CPython declares their ABC-ness in python (Lib/io.py wraps them with abc.ABCMeta and
# virtually registers the concrete classes) but implements every default in C. SharpPy had no
# counterpart, which is why Lib/io.py had to carry about 200 python lines for that layer.
# Measured against CPython 3.12.15. io.IOBase.__mro__ still reads
# ['IOBase','ABC','object'] instead of ['IOBase','_IOBase','object'] until Lib/io.py itself is
# rewritten to CPython's shape (step B4).


import _io
import io


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:60])


names = ["_IOBase", "_RawIOBase", "_BufferedIOBase", "_TextIOBase",
         "DEFAULT_BUFFER_SIZE", "open", "BlockingIOError"]
s("missing", lambda: [n for n in names if not hasattr(_io, n)])
s("buffer_size", lambda: _io.DEFAULT_BUFFER_SIZE)
s("iobase_mro", lambda: [c.__name__ for c in _io._IOBase.__mro__])
s("raw_mro", lambda: [c.__name__ for c in _io._RawIOBase.__mro__])
s("buf_mro", lambda: [c.__name__ for c in _io._BufferedIOBase.__mro__])
s("text_mro", lambda: [c.__name__ for c in _io._TextIOBase.__mro__])
s("bases", lambda: ([c.__name__ for c in _io._RawIOBase.__bases__],
                    [c.__name__ for c in _io._TextIOBase.__bases__]))
b = _io._IOBase()
s("type_name", lambda: type(b).__name__)
s("module", lambda: type(b).__module__)
s("closed", lambda: b.closed)
s("isatty", lambda: b.isatty())
s("flush", lambda: b.flush())
s("flags", lambda: (b.readable(), b.writable(), b.seekable()))
s("fileno", lambda: b.fileno())
s("read", lambda: b.read())
s("readline", lambda: b.readline())
s("readlines", lambda: b.readlines())
s("writelines_empty", lambda: b.writelines([]))
s("writelines_str", lambda: b.writelines("ab"))
s("truncate", lambda: b.truncate())
s("seek", lambda: b.seek(0))
s("tell", lambda: b.tell())
s("iter_is_self", lambda: iter(b) is b)
s("next", lambda: next(b))
s("enter", lambda: b.__enter__() is b)
s("dict_attr", lambda: (setattr(b, "x", 5), b.x))
s("close", lambda: (b.close(), b.closed))
s("closed_isatty", lambda: b.isatty())
s("closed_flush", lambda: b.flush())
s("closed_enter", lambda: b.__enter__())
s("closed_iter", lambda: iter(b))
s("closed_setattr", lambda: setattr(b, "y", 1))
t = _io._TextIOBase()
s("text_isatty", lambda: t.isatty())
s("text_read", lambda: t.read())
s("text_write", lambda: t.write("a"))
r = _io._RawIOBase()
s("raw_read", lambda: r.read())
s("raw_readall", lambda: r.readall())
s("raw_write", lambda: r.write(b"a"))
s("raw_close", lambda: r.close())
# The public ABCs declared in Lib/io.py on top of these native bases.
s("iobase_chain", lambda: [c.__name__ for c in __import__("io").IOBase.__mro__])
s("text_chain", lambda: [c.__name__ for c in __import__("io").TextIOBase.__mro__])
# A subclass of the native base is now instantiable and reports the C chain. This was skipped
# before step B4, when the public ABCs were python-only and inheriting a native base was broken.
s("subclass_native", lambda: [c.__name__ for c in type(
    type("T", (_io._TextIOBase,), {})()).__mro__])
s("repr_shape", lambda: type(repr(_io._IOBase())).__name__)
s("abstract_msg_raw", lambda: _io._RawIOBase().readinto(bytearray(2)))
