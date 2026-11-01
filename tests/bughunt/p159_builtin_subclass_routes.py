class L(list):
    def __iter__(self):
        return iter([1, 2])

    def __contains__(self, v):
        return True

    def __add__(self, o):
        return 7

    def __getitem__(self, i):
        return super().__getitem__(i) if isinstance(i, slice) else 9


l = L([3, 4])
a, b = l
print(list(l), tuple(l), [*l], (*l,), a, b, l + [1], l[0], list.__contains__(l, 5), list.__len__(l), list.__eq__(l, [3, 4]), print(*l))


class T(tuple):
    def __iter__(self):
        return iter((7, 8))

    def __getitem__(self, i):
        return 0


t = T((1, 2))
x, y = t
print(x, y, list(t), t[0], tuple.__getitem__(t, 1), (lambda *p: p)(*t), tuple.__contains__(t, 7))


class D(dict):
    def __iter__(self):
        return iter(["k"])

    def keys(self):
        return ["k"]

    def __getitem__(self, k):
        return 1


class Plain(dict):
    def keys(self):
        return ["never"]


print(dict(D(a=2)), {**D(a=2)}, (lambda **k: k)(**D(a=2)), dict(Plain(a=2)), {**Plain(a=2)})


class St(str):
    def __iter__(self):
        return iter("zz")

    def __add__(self, o):
        return "A"

    def __getitem__(self, i):
        return super().__getitem__(0) + "!"

    def __hash__(self):
        return 1

    def __eq__(self, o):
        return True

    def __str__(self):
        return "S"

    def __repr__(self):
        return "R"


s = St("ab")
print(list(s), s + "x", "x" + s, s[1], str(s), repr(s), f"{s}", "%s" % s, "".join(s), "-".join([s, s]), s.upper(), str.__len__(s), hash(s), {s: 1}, 2 * s, ord(St("a")))


class Sx(set):
    def __iter__(self):
        return iter([5])

    def __len__(self):
        return super().__len__() + 1

    def __contains__(self, x):
        return super().__contains__(x) or x == 9


q = Sx([1, 2])
print(list(q), len(q), 9 in q, set(q), frozenset(q), set.__len__(q), set.__or__(q, {3}), {3} | q)


class Base:
    def __getattr__(self, n):
        return "got " + n


class Sub(Base):
    pass


class M(type):
    def __getattr__(cls, n):
        return "meta " + n


class C(metaclass=M):
    pass


print(Sub().q, C.q, hasattr(C(), "q"))


def nested():
    a = 1

    def f():
        nonlocal a
        a = 2

        def g():
            nonlocal a
            a = 3
        g()
        return a
    return f(), a


print(nested(), f"{10:b}", f"{1.5:f}", f"b{1}", rf"a\t{1}", object.__repr__(L()).split(" ")[0], hasattr(zip(), "__repr__"), [1].__str__(), (lambda: 0).__call__())
print(sorted(dir(type("E", (), {})())) == sorted(dir(object()) + ["__dict__", "__module__", "__weakref__"]), bool(0j), bool(range(0)), {}.keys() & {1}, {1: 2}.keys() | {3}, {1: 2}.items().isdisjoint([]), list(reversed({1: 2, 3: 4})), iter([1, 2]).__length_hint__(), range(5).index(3), range(5).count(9), (1 + 2j).__add__(1), (1j).__radd__(2), b"a".__mod__(()), {1}.__and__({1}), (5).__rpow__(2), (5).is_integer())
for code in ("f(a=1, a=2)", "a, *b, *c = x", "match x:\n    case y:\n        pass\n    case 1:\n        pass\n", "match x:\n    case _:\n        pass\n    case 1:\n        pass\n"):
    try:
        compile(code, "<t>", "exec")
    except SyntaxError as e:
        print(e.msg)
try:
    try:
        raise ValueError
    except ((ValueError, TypeError), KeyError):
        pass
except TypeError as e:
    print(e)
try:
    a, b = 1, 2, 3
except ValueError as e:
    print(e)
