# Private name mangling is a compile-time rewrite (Python/compile.c _Py_Mangle, used by the compiler and
# Python/symtable.c): inside class C, an identifier __x that does not end in __ and has no dot is _C__x.
class C:
    __a = 1
    __dunder__ = 2
    __trail_ = 3

    def __init__(self):
        self.__b = 2

    def __m(self):
        return "C.__m"

    def get(self):
        return self.__a, self.__b, C.__a, self.__m()

    def param(self, __p=4):
        return __p, sorted(locals())

    def nested(self):
        def inner():
            return self.__b

        return inner(), (lambda: self.__b)(), [self.__b for _ in (1,)]

    class __Inner:
        __z = 5

        def z(self):
            return self.__z

    def make(self):
        return self.__Inner().z()


c = C()
print(sorted(k for k in vars(C) if k.startswith("_") and "weakref" not in k and k[1] != "_"))
print(c.__dict__, c.get(), c._C__b, C._C__a, c._C__m())
print(hasattr(c, "__b"), hasattr(C, "__a"), C.__dunder__, C._C__trail_)
print(c.param(), c.nested(), c.make(), C._C__Inner._C__Inner__z if False else C._C__Inner._Inner__z)


class D(C):
    def __m(self):
        return "D.__m"

    def other(self):
        return self.__m()


print(D().get(), D().other())


class Base:
    def __init__(self):
        self.__x = "base"

    def base(self):
        return self.__x


class Derived(Base):
    def __init__(self):
        super().__init__()
        self.__x = "derived"

    def derived(self):
        return self.__x


d = Derived()
print(d.base(), d.derived(), sorted(d.__dict__))


class _Under:
    __u = 1


class __:
    __v = 1


print(sorted(k for k in vars(_Under) if "u" in k and "_Under" in k), "__v" in vars(__))


def outside():
    return C.__name__


__g = 7


class G:
    def read(self):
        global __g
        return __g

    def k(self, **kw):
        return kw

    def kw(self):
        return self.k(__q=1)

    def imp(self):
        import os as __os
        return __os.sep, sorted(locals())

    def getattr_str(self):
        return getattr(self, "__nope", "plain string is not mangled")


try:
    print(G().read())
except NameError as x:
    print(x)
print(G().kw(), G().imp()[1], G().getattr_str(), G.read.__name__, G.__qualname__)


class H:
    def __private(self):
        pass

    print(__private.__name__, __private.__qualname__)


class I:
    def plain_import(self):
        import os
        import os.path
        import os.path as p
        from os import sep
        from os import sep as s2
        return os.sep == sep == s2, p.sep == sep

    def except_plain(self):
        try:
            raise ValueError(1)
        except ValueError:
            return "caught"

    import sys as __sys
    import sys


print(I().plain_import(), I().except_plain(), I._I__sys is I.sys)
