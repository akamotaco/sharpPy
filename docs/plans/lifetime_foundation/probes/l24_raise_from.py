# L2.4 acceptance. raise ... from and with_traceback on the exceptions that are not Exception.
# RAISE_VARARGS must not turn them into Exception, and SystemExit stays a SystemExit. Expected status 3.
for T in (SystemExit, KeyboardInterrupt, GeneratorExit, BaseException):
    try:
        raise T(3) from ValueError(1)
    except BaseException as e:
        print(type(e).__name__, e.args, repr(e.__cause__), e.__suppress_context__)
    try:
        raise T(4) from None
    except BaseException as e:
        print(type(e).__name__, e.args, e.__cause__, e.__suppress_context__)
    try:
        raise T(5).with_traceback(None)
    except BaseException as e:
        print(type(e).__name__, e.args, e.__traceback__ is not None)
    t = T(6)
    print(t.with_traceback(None) is t)

try:
    try:
        raise ValueError(1)
    except ValueError as v:
        raise SystemExit(7) from v
except SystemExit as e:
    print(e.code, repr(e.__cause__), repr(e.__context__))

try:
    raise ValueError(1)
except ValueError:
    raise SystemExit(3) from None
