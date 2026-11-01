def g(a):
    def h(c):
        a(c)
        return lambda: c
    return h


print(g(bool)(1)())


def deco(fn):
    def wrapper(x):
        def later():
            return x + 1
        return fn(later())
    return wrapper


@deco
def twice(v):
    return v * 2


print(twice(3))


def many(a, b, c):
    def h(d, e):
        f = lambda: (d, e)
        return a(b(c(d))), f()
    return h


print(many(str, abs, int)(-2, 3))


def rec(n):
    def go(k):
        keep = lambda: k
        return keep() if k <= 0 else go(k - 1) + n
    return go(3)


print(rec(2))


def comp(fn):
    def h(items):
        last = [fn(i) for i in items]
        return last, (lambda: items)()
    return h


print(comp(str)([1, 2]))


def callcell():
    f = len
    def inner():
        return f("abc")
    return f("ab"), inner()


print(callcell())


def localcall():
    f = len
    return f("ab"), [f(x) for x in ["a", "bb"]]


print(localcall())


def shadow():
    print = str
    return print(1)


print(shadow())


def glob():
    return abs(-1), [abs(x) for x in [-2]], (lambda: abs(-3))()


print(glob())


class K:
    f = len
    v = f("ab")
    w = [abs(x) for x in [-1]]


print(K.v, K.w)
cell = (lambda x: (lambda: x))(5).__closure__[0]
print(cell.cell_contents)
cell.cell_contents = 6
print(cell.cell_contents)


def nl():
    t = 1
    def h(c):
        nonlocal t
        keep = lambda: c
        t += c
        del_me = t
        return t, keep(), del_me
    return h


print(nl()(2))


class Base:
    def m(self):
        return "base"


class Sub(Base):
    def m(self):
        c = 1
        keep = lambda: c
        return super().m(), keep()

    def n(self, v):
        keep = lambda: v
        return [super().m() for _ in range(1)], keep()

    def o(self, v):
        keep = lambda: v
        return __class__.__name__, keep()


print(Sub().m(), Sub().n(1), Sub().o(2))
