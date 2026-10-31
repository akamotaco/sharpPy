# L2.4 acceptance. BaseException_new stores args before __init__ runs (Objects/exceptions.c:56), so an
# __init__ that does not call super() still has them. repr reads the same args the attribute shows,
# raised or not. Registered gap P0-59 is not closed until this file matches.
class X(Exception):
    def __init__(self, a):
        self.a = a


print(X(1).args, repr(X(1)), str(X(1)), X(1).a)


class Y(Exception):
    def __init__(self, a):
        super().__init__()


print(Y(1).args, repr(Y(1)), repr(str(Y(1))))


class Z(Exception):
    def __init__(self, a):
        super().__init__(a, a)


print(Z(1).args, repr(Z(1)))


class Kw(Exception):
    def __init__(self, a=1, *, b=2):
        pass


print(Kw(5).args, Kw(a=5).args, Kw(5, b=6).args, repr(Kw(a=5)))


class New(Exception):
    def __new__(cls, *a):
        return super().__new__(cls, "n")

    def __init__(self, *a):
        pass


print(New(1, 2).args, repr(New(1, 2)))


class Key(KeyError):
    def __init__(self, k):
        pass


print(Key("a").args, str(Key("a")), repr(Key("a")))


class NoInit(SystemExit):
    def __init__(self, x):
        pass


print(NoInit(5).args, NoInit(5).code)

e = SystemExit.__new__(SystemExit, 5)
print(e.args, e.code)
print(BaseException.__new__(ValueError, 1, 2).args)


class K(KeyboardInterrupt):
    def __init__(self):
        super().__init__("z")


class W(Exception):
    def __init__(self):
        super().__init__("z")


for raised in (K, K(), W, W(), X(1), Y(1), Z(1)):
    try:
        raise raised
    except BaseException as caught:
        print(repr(caught), caught.args, str(caught))

for bad in (1, "s", None):
    try:
        BaseException.__repr__(bad)
    except TypeError as x:
        print("TypeError:", x)
