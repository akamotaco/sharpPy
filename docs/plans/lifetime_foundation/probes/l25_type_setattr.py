# L2.5 route: an assignment on a class goes through type_setattro, which asks the metatype for a data
# descriptor before it touches the class dict (Objects/typeobject.c type_setattro,
# _PyObject_GenericSetAttrWithDict). L2.4 moved __qualname__ out of the dict, and the assignment still
# wrote the dict, so the value was lost.
import functools


class Q:
    pass


Q.__qualname__ = "R"
print(Q.__qualname__, Q.__name__, repr(Q), "__qualname__" in Q.__dict__)

try:
    Q.__qualname__ = 5
except TypeError as x:
    print(x)
print(Q.__qualname__)

try:
    del Q.__qualname__
except (TypeError, AttributeError) as x:
    print(type(x).__name__, x)
print(Q.__qualname__)


def deco(cls):
    class W(cls):
        pass

    functools.update_wrapper(W, cls, updated=())
    return W


@deco
class Orig:
    pass


print(Orig.__name__, Orig.__qualname__)


class Plain:
    pass


Plain.x = 1
Plain.__doc__ = "d"
Plain.__module__ = "m"
print(Plain.x, Plain.__doc__, Plain.__module__, "x" in Plain.__dict__)
del Plain.x
print(hasattr(Plain, "x"))
print(Plain.__qualname__)
