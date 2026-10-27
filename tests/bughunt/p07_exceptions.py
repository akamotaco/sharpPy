def show(label, fn):
    try:
        print(f"{label!r}: {fn()!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e} | args={e.args!r}")

def ret_finally():
    try:
        return "try"
    finally:
        return "finally"
show("return.in.finally", ret_finally)

def exc_finally():
    try:
        1 / 0
    finally:
        pass
    return "never"
show("exc.thru.finally", exc_finally)

def chain():
    try:
        try:
            1 / 0
        except ValueError:
            pass
    except ZeroDivisionError as e:
        return (type(e).__name__, type(e.__context__).__name__, e.__suppress_context__)
show("context.set", chain)

def suppress():
    try:
        try:
            1 / 0
        except ZeroDivisionError:
            raise KeyError("k") from None
    except KeyError as e:
        return (e.args, e.__context__, e.__suppress_context__)
show("raise.from.None", suppress)

def implicit_chain():
    try:
        try:
            1 / 0
        except ZeroDivisionError:
            int("x")
    except ValueError as e:
        return (type(e).__name__, type(e.__context__).__name__)
show("implicit.chain", implicit_chain)

def except_order():
    class MyErr(RuntimeError, ValueError):
        pass
    try:
        raise MyErr()
    except ValueError:
        return "caught as ValueError"
show("multi.inherit.catch", except_order)

def nested_try():
    out = []
    try:
        try:
            raise IndexError
        except IndexError:
            out.append("inner")
            raise KeyError
    except KeyError:
        out.append("outer")
    return out
show("re-raise.nested", nested_try)

def finally_break():
    while True:
        try:
            break
        finally:
            out = "ran"
    return out
show("break.in.try", finally_break)

def continue_finally():
    seen = []
    for i in range(3):
        try:
            if i == 1:
                continue
        finally:
            seen.append(i)
    return seen
show("continue.with.finally", continue_finally)

def reraise_bare():
    try:
        1 / 0
    except ZeroDivisionError:
        try:
            raise
        except ZeroDivisionError as e:
            return ("caught again", e.args)
show("bare.raise", reraise_bare)

def raise_from_outer():
    try:
        try:
            1 / 0
        except ZeroDivisionError:
            raise TypeError("t") from None
    except TypeError as e:
        return e.__cause__, e.__context__, e.__suppress_context__
show("cause.none.context.kept", raise_from_outer)

def except_raises():
    try:
        raise ValueError
    except ValueError:
        raise TypeError
show("handler.raises", except_raises)

def finally_overrides():
    try:
        try:
            raise ValueError
        except TypeError:
            pass
        finally:
            raise RuntimeError
    except RuntimeError as e:
        return (type(e).__name__, type(e.__context__).__name__)
show("finally.replaces.unhandled", finally_overrides)

def except_star_basic():
    out = []
    try:
        try:
            1 / 0
        except* ZeroDivisionError:
            out.append("except*-caught")
    except Exception as e:
        out.append(f"Escaped {type(e).__name__}")
    return out
show("except.star.single", except_star_basic)

def except_star_group():
    out = []
    try:
        try:
            raise ExceptionGroup("g", [ValueError("v"), KeyError("k")])
        except* KeyError as tg:
            out.append(("tg", type(tg).__name__, [str(x) for x in tg.exceptions]))
        except* ValueError as tg:
            out.append(("tg-ve", type(tg).__name__, len(tg.exceptions)))
    except BaseException as e:
        out.append(("left", type(e).__name__, len(getattr(e, 'exceptions', ()))))
    return out
show("except.star.subgroup", except_star_group)

class CustomExc(Exception):
    def __init__(self, *a):
        super().__init__(*a)
        self.note_extra = True
show("exc.subclass.args", lambda: CustomExc(1, "two").args)
show("exc.new.no.args", lambda: type("E", (Exception,), {})().args)
show("exc.with.from.none", lambda: (e := ValueError("x"), e.with_traceback(None), e.__traceback__)[2])
show("exc.str", lambda: str(ValueError("a", "b")))
show("exc.repr", lambda: repr(ValueError("a", "b")))
show("exc.picklable.name", lambda: [BaseException.__name__, Exception.__base__ is BaseException, ZeroDivisionError.__mro__[1] is ArithmeticError])
show("assert.raises", lambda: (lambda: (_ for _ in ()).throw(AssertionError("msg")))())
try:
    assert False, "assert-msg"
except AssertionError as e:
    print(f"assert.msg: {e}")
def exc_attr():
    try:
        raise OSError(2, "no such", "filename", 42, "detail")
    except OSError as e:
        return (e.errno, e.strerror, e.filename, e.filename2)
show("oserror.fields", exc_attr)
