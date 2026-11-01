from typing import Generic, Protocol, TypeVar


class Plain:
    tag = "plain"


class Box[T](Plain):
    item: T

    def __init__(self, item: T) -> None:
        self.item = item

    def get(self) -> T:
        return self.item

    def kind(self):
        return T.__name__, __class__.__name__


print(Box.__bases__, Box.__mro__, Box.tag, Box(1).get(), Box(1).kind(), Box.__type_params__, Box.__orig_bases__, Box.__parameters__)
print(Box[int], Box[int](2).get(), Box.get.__annotations__, Box.__annotations__)


class Pair[K, V: int, *Ts, **P]:
    first: K
    second: V

    def swap(self, k: K, v: V) -> tuple[V, K]:
        return v, k

    def names(self):
        return K.__name__, V.__name__, V.__bound__, Ts.__name__, P.__name__


print(Pair.__type_params__, Pair.__mro__, Pair().names(), Pair.__parameters__, Pair.__orig_bases__, Pair.__annotations__)


class Meta(type):
    def __new__(mcs, name, bases, ns, **kw):
        cls = super().__new__(mcs, name, bases, ns)
        cls.kw = kw
        return cls


class WithKw[T](metaclass=Meta, flag=1):
    pass


print(type(WithKw), WithKw.kw, WithKw.__type_params__)


class Hooked:
    def __init_subclass__(cls, tag=None, **kw):
        super().__init_subclass__(**kw)
        cls.got = tag


class Sub[T](Hooked, tag="x"):
    pass


print(Sub.got, Sub.__bases__)


class Proto[T](Protocol):
    def m(self) -> T: ...


class OldBase[T]:
    pass


class Derived[T](OldBase[T]):
    pass


class Bound[T](Box[T], Plain):
    pass


print(Proto.__mro__[1:3], Proto._is_protocol, Derived.__mro__, Derived.__orig_bases__, Derived.__parameters__, Derived[int], Bound.__bases__)


def outer(x):
    class Local[T]:
        value = x

        def get(self) -> T:
            return x, T.__name__

    return Local


L = outer(5)
print(L.value, L().get(), L.__qualname__, L.__type_params__, L.__module__)


def generic[T, U](a: T, b: U = 1) -> tuple[T, U]:
    return (a, b), T.__name__, U.__name__


print(generic(1), generic.__type_params__, generic.__annotations__)


class Outer:
    class Inner[T]:
        x = 1

    def method[T](self, a: T) -> T:
        return a


print(Outer.Inner.__qualname__, Outer.Inner.__type_params__, Outer().method(3), Outer.method.__qualname__, Outer.method.__type_params__)
print(Plain.__dict__.get("__type_params__", "absent"), Plain.__type_params__, int.__type_params__ if hasattr(int, "__type_params__") else "none", "__type_params__" in Box.__dict__)
T2 = TypeVar("T2")


class Old(Generic[T2]):
    pass


print(Old.__type_params__, Old.__parameters__, Old[int], Old.__orig_bases__)
try:
    class Bad[T](Generic[T]):
        pass
except TypeError as e:
    print(e)
