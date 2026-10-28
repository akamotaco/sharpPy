# p17: traceback, frame and code-object parity
import io
import sys
import traceback


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# ---------------------------------------------------------------- traceback
def outer():
    middle()


def middle():
    inner()


def inner():
    raise ValueError("boom")


try:
    outer()
except Exception as e:
    tb = e.__traceback__
    buf = io.StringIO()
    traceback.print_exception(type(e), e, tb, file=buf)
    text = buf.getvalue()
    show("print_exc.head", lambda: text.splitlines()[0])
    show("print_exc.last", lambda: text.splitlines()[-1])
    show("print_exc.count", lambda: len(text.splitlines()))
    show("print_exc.lineno_lines", lambda: [l.split(",")[1].strip() for l in text.splitlines() if l.strip().startswith("File ")])

    show("format_exc_type", lambda: traceback.format_exc().splitlines()[-1])

    frames = []
    t = tb
    while t is not None:
        frames.append((t.tb_lineno, t.tb_frame.f_code.co_name))
        t = t.tb_next
    show("tb.walk", lambda: frames)

    show("extract_tb.names", lambda: [fr.name for fr in traceback.extract_tb(tb)])
    show("format_tb.last", lambda: traceback.format_tb(tb)[-1].splitlines()[-1].strip())

    # traceback object set via with_traceback
    e2 = RuntimeError("manual")
    show("with_traceback.ret", lambda: type(e2.with_traceback(tb)).__name__)
    show("with_traceback.tb", lambda: e2.with_traceback(tb).__traceback__.tb_lineno)

# exception raised inside except -> context chain in format
try:
    try:
        1 / 0
    except ZeroDivisionError:
        raise KeyError("ctx")
except KeyError as e:
    buf = io.StringIO()
    traceback.print_exception(type(e), e, e.__traceback__, file=buf)
    lines = buf.getvalue().splitlines()
    show("ctx.markers", lambda: [l for l in lines if "During handling" in l or "The above exception" in l])
    show("ctx.tail", lambda: lines[-1])

try:
    try:
        1 / 0
    except ZeroDivisionError as z:
        raise KeyError("cause") from z
except KeyError as e:
    buf = io.StringIO()
    traceback.print_exception(type(e), e, e.__traceback__, file=buf)
    lines = buf.getvalue().splitlines()
    show("cause.markers", lambda: [l for l in lines if "During handling" in l or "The above exception" in l])
    show("cause.__cause__", lambda: type(e.__cause__).__name__)

# ---------------------------------------------------------------- frames
def frame_probe(a, b=2):
    c = a + b
    fr = sys._getframe()
    return (
        fr.f_code.co_name,
        sorted(fr.f_locals.keys()),
        fr.f_locals["c"],
        type(fr.f_globals).__name__,
        "frame_probe" in fr.f_globals,
        fr.f_lineno > 0,
        fr.f_back is None,
    )


show("frame.self", lambda: frame_probe(1))


def frame_back():
    return helper_back()


def helper_back():
    fr = sys._getframe()
    return (fr.f_code.co_name, fr.f_back.f_code.co_name, fr.f_back.f_back is None or fr.f_back.f_back.f_code.co_name)


show("frame.back", lambda: frame_back())


def frame_depth():
    try:
        sys._getframe(5)
    except ValueError as e:
        return str(e)


show("frame.depth", frame_depth)
show("frame.depth0", lambda: sys._getframe(0).f_code.co_name)
show("frame.module_builtins", lambda: "print" in sys._getframe(0).f_builtins)


def frame_locals_live():
    x = 1

    def mutate():
        fr = sys._getframe(1)
        fr.f_locals["x"] = 99

    mutate()
    return x


show("frame.locals.writeback", frame_locals_live)


class FrameHolder:
    pass


def frame_type_name():
    fr = sys._getframe()
    h = FrameHolder()
    try:
        h.f_code
    except AttributeError as e:
        err = str(e)
    return (type(fr).__name__, repr(fr).startswith("<frame "), err)


show("frame.obj", frame_type_name)

# ---------------------------------------------------------------- code objects
def target(p, q=1, *args, r=2, **kw):
    """docstring"""
    local = p
    return local


code = target.__code__
show("code.has", lambda: type(code).__name__)
show("code.name", lambda: code.co_name)
show("code.argcount", lambda: code.co_argcount)
show("code.kwonly", lambda: code.co_kwonlyargcount)
show("code.varnames", lambda: tuple(code.co_varnames))
show("code.filename.endswith", lambda: code.co_filename.endswith("p17_traceback_frame.py"))
show("code.firstlineno.pos", lambda: code.co_firstlineno > 0)
show("code.doc", lambda: target.__doc__)
show("code.name_attr", lambda: code.co_name)

show("code.name_ro", lambda: (setattr(code, "co_name", "x"), None)[1])

show("code.qualname", lambda: getattr(code, "co_qualname", "MISSING"))

# lambda / nested qualnames via repr of function
show("code.fn_repr", lambda: repr(target).split(" at ")[0])

# generator frames
def gen():
    frame = sys._getframe()
    yield frame.f_code.co_name


show("gen.frame", lambda: next(gen()))

# sys.exc_info / traceback types
try:
    raise IndexError("ei")
except IndexError:
    show("exc_info.types", lambda: tuple(type(x).__name__ for x in sys.exc_info()))
    show("tb.repr", lambda: repr(sys.exc_info()[2]).startswith("<traceback object at"))
    show("tb.type", lambda: type(sys.exc_info()[2]).__name__)

# TracebackException
try:
    raise ValueError("te")
except ValueError as e:
    te = traceback.TracebackException.from_exception(e)
    show("te.stack", lambda: [s.name for s in te.stack])
    show("te.exc_type", lambda: te.exc_type.__name__)
    show("te.format_exc_last", lambda: list(te.format_exception_only())[-1].rstrip())
