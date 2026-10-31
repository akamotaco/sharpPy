# An object passed through a constructor or a function that defines a closure must not be kept by what
# was left behind. func_new keeps code, cells, defaults and globals, and an instance keeps no arguments.
import _weakref as w
import gc


class A:
    pass


def dead(make):
    o = A()
    r = w.ref(o)
    kept = make(o)
    del o
    gc.collect()
    return r() is None, kept is not None


class Ctor:
    def __init__(self, obj):
        self.n = 1


class Star:
    def __init__(self, *args, **kwargs):
        self.n = len(args)


class Over:
    def __new__(cls, obj):
        return object.__new__(cls)

    def __init__(self, obj):
        pass


def closure_unused(x):
    def cb():
        return 1

    return cb


def closure_other(x):
    y = 2

    def cb():
        return y

    return cb


def lambda_unused(x):
    return lambda: 1


def gen_unused(x):
    def g():
        yield 1

    return g


async def co_factory(x):
    return 1


def async_unused(x):
    async def c():
        return 1

    return c


print(dead(Ctor), dead(Star), dead(Over))
print(dead(closure_unused), dead(closure_other), dead(lambda_unused), dead(gen_unused), dead(async_unused))


def closure_used(x):
    def cb():
        return x

    return cb


o = A()
r = w.ref(o)
c = closure_used(o)
del o
gc.collect()
print(r() is c(), r() is None)
del c
gc.collect()
print(r())
