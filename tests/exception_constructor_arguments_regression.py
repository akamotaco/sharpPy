"""BaseException-style positional argument construction, CPython 3.12 oracle."""
from builtins import SystemError, RecursionError, LookupError
ArithmeticError = ZeroDivisionError.__bases__[0]

types = (BaseException, Exception, TypeError, ValueError, ArithmeticError,
         ZeroDivisionError, OverflowError, LookupError, IndexError, RuntimeError,
         SystemError, NotImplementedError, RecursionError, AssertionError,
         GeneratorExit)
for cls in types:
    for args in ((), ('',), ('message',), (17,), (None,), ('a', 2), ((1, 2),)):
        error = cls(*args)
        print(cls.__name__, error.args, str(error), repr(error), error.args is error.args)
        assert error.args == args
    try:
        cls(message='keyword')
    except TypeError as error:
        print('keyword', str(error))
    else:
        raise AssertionError('keyword accepted')

# Construction must retain the objects without eagerly invoking __str__/__repr__.
events = []
class Argument:
    def __str__(self):
        events.append('str')
        return 'argument'
    def __repr__(self):
        events.append('repr')
        return 'Argument()'
obj = Argument()
for cls in (BaseException, Exception, ValueError, GeneratorExit):
    events.clear()
    error = cls(obj)
    print('identity', cls.__name__, error.args[0] is obj, events)
    print('format', str(error), repr(error), events)
    error.args = ()
    print('cleared', str(error), repr(error))

# Explicit empty strings remain distinct from a missing argument, including throw.
def generator():
    try:
        yield None
    except ValueError as error:
        print('caught-args', error.args)
        return 19
for value in (ValueError, ValueError('')):
    g = generator()
    next(g)
    try:
        g.throw(value)
    except StopIteration as error:
        print('result', error.value)
    finally:
        g.close()
