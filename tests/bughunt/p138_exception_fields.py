import errno


def show(e, *names):
    print(type(e).__name__, repr(e), str(e), e.args, [getattr(e, n, "?") for n in names])


class MyOS(OSError):
    pass


class MyOS2(OSError):
    def __init__(self, a, b):
        super().__init__(a, b)
        self.extra = 1


show(OSError(2, "gone"), "errno", "strerror", "filename", "filename2")
show(OSError(2, "gone", "f.txt"), "errno", "strerror", "filename", "filename2")
show(OSError("one"), "errno", "strerror", "filename")
show(OSError(), "errno", "strerror")
show(MyOS(13, "denied", "x"), "errno", "strerror", "filename")
show(MyOS2(13, "denied"), "errno", "strerror", "extra")
show(FileNotFoundError(errno.ENOENT, "missing", "a"), "errno", "strerror", "filename")
print(type(OSError(errno.ENOENT, "x")).__name__, type(OSError(errno.EEXIST, "x")).__name__,
      type(OSError(errno.EACCES, "x")).__name__, type(OSError(999, "x")).__name__)
print(type(MyOS(errno.ENOENT, "x")).__name__, isinstance(OSError(errno.ENOENT, "x"), FileNotFoundError))
print(IOError is OSError, EnvironmentError is OSError)
e = OSError(1, "a")
e.errno = 5
e.strerror = "b"
e.filename = "c"
print(e, e.args)
try:
    raise MyOS(2, "boom", "file")
except OSError as x:
    show(x, "errno", "strerror", "filename")


class MyStop(StopIteration):
    pass


show(StopIteration(), "value")
show(StopIteration(5), "value")
show(StopIteration(5, 6), "value")
show(MyStop(7), "value")


def g():
    yield 1
    return 9


it = g()
next(it)
try:
    next(it)
except StopIteration as x:
    show(x, "value")


class MyImp(ImportError):
    pass


show(ImportError("m", name="n", path="p"), "name", "path", "msg")
show(ImportError(), "name", "path", "msg")
show(MyImp("m", name="n"), "name", "path", "msg")
show(ModuleNotFoundError("m", name="n"), "name", "path", "msg")
try:
    import not_a_module_at_all
except ImportError as x:
    show(x, "name", "msg")


class MyAttr(AttributeError):
    pass


show(AttributeError("a", name="n", obj=3), "name", "obj")
show(MyAttr("a", name="n"), "name", "obj")
show(NameError("a", name="n"), "name")
try:
    undefined_name
except NameError as x:
    show(x, "name")


class MySyn(SyntaxError):
    pass


show(SyntaxError("m", ("f", 1, 2, "t")), "msg", "filename", "lineno", "offset", "text", "end_lineno", "end_offset")
show(SyntaxError("m", ("f", 1, 2, "t", 3, 4)), "lineno", "end_lineno", "end_offset")
show(SyntaxError("m"), "msg", "filename", "lineno")
show(MySyn("m", ("f", 1, 2, "t")), "msg", "lineno", "text")
show(IndentationError("m", ("f", 1, 2, "t")), "msg", "lineno")
try:
    compile("def", "<s>", "exec")
except SyntaxError as x:
    print(type(x).__name__, x.filename)


class MyDec(UnicodeDecodeError):
    pass


show(UnicodeDecodeError("utf-8", b"abc", 1, 2, "bad"), "encoding", "object", "start", "end", "reason")
show(UnicodeEncodeError("ascii", "abc", 1, 2, "bad"), "encoding", "object", "start", "end", "reason")
show(UnicodeTranslateError("abc", 1, 2, "bad"), "object", "start", "end", "reason")
show(MyDec("utf-8", b"abc", 1, 2, "bad"), "encoding", "start", "reason")
try:
    b"\xff".decode("utf-8")
except UnicodeDecodeError as x:
    show(x, "encoding", "start", "end", "reason")
try:
    "가".encode("ascii")
except UnicodeEncodeError as x:
    show(x, "encoding", "start", "end", "reason")


class SE(SystemExit):
    pass


show(SystemExit(), "code")
show(SystemExit(3), "code")
show(SystemExit(1, 2), "code")
show(SE("bye"), "code")
show(KeyError("k"), "args")
show(KeyError(), "args")
show(KeyError(1, 2), "args")
