# L2.3 acceptance, written by the reviewer. The harness compares stdout and the exit status, so each
# status has its own script: l23_exit_*.py. Here: SystemExit.code is a stored field, not a view of
# args; a subclass is a SystemExit; the status is decided before atexit runs. Expected status 7.
import atexit

e = SystemExit(1, 2)
print(e.code, e.args, str(e), repr(e))
e.args = (9,)
print("args do not move code:", e.code, e.args)
e.code = 5
print("code does not move args:", e.code, e.args)
del e.code
print("deleted reads as None:", e.code, e.args)


class Sub(SystemExit):
    pass


class Init(SystemExit):
    def __init__(self):
        super().__init__(6)


print(Init().code, Sub("x", "y").code, Sub().code)

try:
    exit(1, 2)
except TypeError as x:
    print("TypeError:", x)

print(repr(KeyboardInterrupt()), KeyboardInterrupt().args, repr(str(KeyboardInterrupt())))
print(repr(KeyboardInterrupt("x")), KeyboardInterrupt("x", 1).args, str(KeyboardInterrupt("x")))


class SubK(KeyboardInterrupt):
    pass


print(repr(SubK(1)), isinstance(SubK(1), Exception))
print(BaseExceptionGroup("m", [KeyboardInterrupt(), SystemExit(1)]).exceptions)

last = Sub(4)
last.args = (9,)
last.code = 7


def late():
    print("atexit runs after the status is fixed", flush=True)
    last.code = 8


atexit.register(late)
raise last
