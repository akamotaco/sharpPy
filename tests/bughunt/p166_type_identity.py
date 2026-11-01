# PyType_IsSubtype and super walk the MRO by identity; two classes may share a name.
class Base:
    def who(self):
        return "base"

    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        cls.marked = True


Outer = Base


class Base(Outer):
    def who(self):
        return "derived " + super().who()


print(Base().who(), Base.marked, [c.__name__ for c in Base.__mro__])
Middle = Base


class Base(Middle):
    def who(self):
        return "top " + super().who()


print(Base().who(), issubclass(Base, Outer), issubclass(Outer, Base), isinstance(Outer(), Middle))


def make():
    class Same:
        pass
    return Same


First, Second = make(), make()
print(First is Second, issubclass(First, Second), isinstance(First(), Second))
try:
    super(First, Second())
except TypeError as e:
    print(e)


class list:
    pass


class tuple:
    pass


print(issubclass(list, __builtins__.list if hasattr(__builtins__, "list") else object), isinstance(list(), __builtins__.tuple if hasattr(__builtins__, "tuple") else object))
print(issubclass(list, tuple), isinstance((), tuple), isinstance([], list), isinstance(list(), object))
try:
    match []:
        case list():
            print("matched mine")
        case _:
            print("not mine")
finally:
    del list, tuple


class int(int):
    pass


print(isinstance(1, int), issubclass(int, object), int(3) + 1, type(int(3) + 1).__name__)
del int
