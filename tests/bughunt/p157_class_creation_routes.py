log = []


class Named:
    def __set_name__(self, owner, name):
        log.append(("set_name", owner.__name__, name))


class Base:
    def __init_subclass__(cls, tag=None, **kw):
        super().__init_subclass__(**kw)
        log.append(("init_subclass", cls.__name__, tag))


class A(Base, tag="a"):
    n = Named()


B = type("B", (Base,), {"n": Named(), "x": 1}, tag="b")
print(log, B.__name__, B.__qualname__, B.__module__, B.__bases__, B.__dict__["x"], type(B))
log.clear()


class Meta(type):
    def __new__(mcs, name, bases, ns, **kw):
        log.append(("meta_new", name, sorted(k for k in ns if not k.startswith("__")), kw))
        return super().__new__(mcs, name, bases, ns)

    def __init__(cls, name, bases, ns, **kw):
        log.append(("meta_init", name, kw))
        super().__init__(name, bases, ns)

    def __call__(cls, *a, **k):
        log.append(("meta_call", cls.__name__, a, k))
        return super().__call__(*a, **k)

    @classmethod
    def __prepare__(mcs, name, bases, **kw):
        log.append(("prepare", name, kw))
        return {"injected": 1}


class C(metaclass=Meta, flag=1):
    y = 2


print(log, C.injected, C.y, type(C), type(C()))
log.clear()
D = Meta("D", (), {"z": 1}, flag=2)
print(log, D.z, type(D), type(D()))
log.clear()
E = type("E", (C,), {})
print(log, type(E), E.__mro__[1].__name__)
log.clear()
F = type(C)("F", (), {})
print(type(F), log)
log.clear()


class Meta2(type):
    pass


class G(metaclass=Meta2):
    pass


for bases, name in (((C, G), "H"),):
    try:
        type(name, bases, {})
    except TypeError as e:
        print(e)
    try:
        class H(C, G):
            pass
    except TypeError as e:
        print(e)


class M3(Meta, Meta2):
    pass


class I(C, G, metaclass=M3):
    pass


print(type(I), type("J", (I,), {}).__class__.__name__)
log.clear()


class K:
    def m(self):
        return __class__.__name__, super().__repr__()[:3]


print(K().m())


def method(self):
    return "plain", self.__class__.__name__


L = type("L", (), {"method": method, "__doc__": "doc", "__slots__": ("a",), "__qualname__": "Q.L", "__module__": "mod"})
print(L().method(), L.__doc__, L.__slots__, L.__qualname__, L.__module__, hasattr(L(), "__dict__"), repr(L)[:30], "__dict__" in L.__dict__, "__weakref__" in L.__dict__)
M = type("M", (), {})
print(sorted(M.__dict__), M.__doc__, M.__module__, M.__qualname__, repr(M))
print(type("N", (int,), {})(5) + 1, type("O", (list,), {})([1]), type("P", (dict,), {})(a=1), type("Q", (Exception,), {})("x").args, type("R", (str,), {"up": lambda s: s.upper()})("a").up())
for args, kw in ((("x",), {}), (("x", ()), {}), ((1, (), {}), {}), (("x", 1, {}), {}), (("x", (), 1), {}), (("x", (1,), {}), {}), (("x", (), {"__slots__": 1}), {}),
                 (("x", (int, str), {}), {}), (("x", (bool,), {}), {}), (("x", (), {}), {"bad": 1}), (("x\0y", (), {}), {}), (("x", (), {"__qualname__": 1}), {})):
    try:
        print(type(*args, **kw))
    except (TypeError, ValueError) as e:
        print(type(e).__name__, e)


def cell_user(self):
    return __class__


class S:
    def __init_subclass__(cls):
        log.append(("S sub", cls.__name__))


class T:
    def __new__(cls):
        return "not T"

    def __init__(self):
        log.append("never")


class U(metaclass=type("Mx", (type,), {"__new__": lambda m, n, b, d: type.__new__(m, n, b, d)})):
    pass


print(T(), log, type(U).__name__, type.__call__(type, 1), type.__new__(type, "V", (), {}).__name__, type.__init__(type("W", (), {}), "a", (), {}))


class X:
    a = 1

    class Y:
        b = 2

        def f(self):
            pass

    def g(self):
        class Z:
            pass
        return Z


print(X.Y.__qualname__, X.Y.f.__qualname__, X().g().__qualname__, X.g.__qualname__, (lambda: 0).__qualname__)
print(X.__bases__, X.__base__, object.__bases__, object.__base__, bool.__base__, int.__mro__, type.__mro__, type.__base__, X.mro())
print(X.__subclasses__(), Base.__subclasses__() == [A, B], type.__subclasses__(type) == [] or True)
X.new = 3
del X.a
print(X.new, hasattr(X, "a"), X.__flags__ & (1 << 9) != 0, int.__flags__ & (1 << 9) != 0)
for call in (lambda: setattr(int, "x", 1), lambda: delattr(int, "real"), lambda: setattr(type, "x", 1), lambda: setattr(object, "x", 1)):
    try:
        call()
    except TypeError as e:
        print(e)
X.__name__ = "XX"
X.__qualname__ = "QQ"
X.__module__ = "mm"
X.__doc__ = "dd"
print(X.__name__, X.__qualname__, X.__module__, X.__doc__, repr(X))


class Y1:
    pass


class Y2:
    pass


class Y3(Y1):
    pass


Y3.__bases__ = (Y2,)
print(Y3.__bases__, Y3.__mro__[1].__name__, issubclass(Y3, Y2), issubclass(Y3, Y1))
