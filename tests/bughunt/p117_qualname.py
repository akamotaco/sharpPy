# L2.4 acceptance. Functions have __qualname__ (Objects/funcobject.c), and method_repr reads it from
# im_func, falling back to __name__, then "?" (Objects/classobject.c:273). method_getattro forwards to
# im_func, so a callable without the name raises.
import functools
import types


def f():
    pass


class H:
    def m(self):
        return 1

    class In:
        def n(self):
            pass


def outer():
    def inner():
        pass

    return inner


h = H()
print(f.__qualname__, (lambda: 1).__qualname__, h.m.__qualname__, H.m.__qualname__)
print(H.In.n.__qualname__, outer().__qualname__, H.__qualname__, len.__qualname__)
print(f.__name__, outer().__name__)

g = outer()
g.__qualname__ = "other"
print(g.__qualname__, g.__name__, repr(g)[:20])
for bad in (1, None):
    try:
        g.__qualname__ = bad
    except TypeError as e:
        print("TypeError:", e)
try:
    del g.__qualname__
except TypeError as e:
    print("TypeError:", e)


@functools.wraps(f)
def wrapped():
    pass


print(wrapped.__qualname__, wrapped.__name__)


class C:
    def __call__(self, *a):
        return a


class Named:
    __name__ = "only_name"

    def __call__(self):
        pass


Method = types.MethodType
print(repr(Method(h.m, 5)), repr(Method(f, 5)), repr(Method(C(), 1)), repr(Method(C, 1)))
print(repr(Method(Named(), 1)))

for name in ("__qualname__", "__name__"):
    try:
        print(getattr(Method(C(), 1), name))
    except AttributeError as e:
        print("AttributeError:", e)

print(hasattr(C(), "__qualname__"), hasattr(C(), "__name__"))
