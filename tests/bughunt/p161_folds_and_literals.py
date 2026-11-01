a, b = -7, 2
print(-7 // 2, a // b, -7 % 2, a % b, 7 // -2, 7 % -2, -7.5 // 2, divmod(-7, 2), -7 / 2, 2 ** -1, -2 ** 2, 1 << 3, ~5, -(-3), 3 - -3, 10 // 3 * 3, 7 // 2.0, -1 // 2)
print([1] and 'truthy', [] and 'x', [] or 'y', [1] or 'z', 0 or 1, 1 and 0, "" or "d", None and 1, (1,) and 2, {} or 3, not [], not [1], 1 if [1] else 2, 2 if [] else 3)
x = [1]
print(x and 'truthy', x or 'y', [x] and 1, [i for i in x] and 'c', {1} and 2, {1: 2} and 3, (x,) and 4)
y = 42.0
print(f"{y=:.2f}", f"{y=}", f"{y = }", f"{y=!r}", f"{y=!s:>6}", f"{y + 1=:.1f}")
print('²'.isdigit(), '²'.isdecimal(), '²'.isnumeric(), '٣'.isdigit(), 'Ⅷ'.isnumeric(), '½'.isnumeric(), '٣'.isdecimal())
f = lambda a, *, b=1: (a, b)
print(f(1), f(1, b=2), (lambda *, k=2: k)(), (lambda *a, b=3: (a, b))(1))
def g(**kw):
    return kw
print(g(x=1, **{"y": 2}, z=3), g(**{"a": 1}, b=2), g(z=3, **{"x": 1, "y": 2}), dict(z=3, **{"x": 1}))
def gen():
    try:
        yield 1
    finally:
        print("cleanup")
def len(x):
    return 99
print(len([1, 2, 3]), len("abc"))
del len
class N:
    def __add__(self, o):
        return "add"
    def __radd__(self, o):
        return "radd"
    def __mul__(self, o):
        return "mul"
    def __rmul__(self, o):
        return "rmul"
    def __sub__(self, o):
        return "sub"
    def __pow__(self, o):
        return "pow"
    def __truediv__(self, o):
        return "div"
    def __or__(self, o):
        return "or"
    def __and__(self, o):
        return "and"
    def __lshift__(self, o):
        return "ls"
n = N()
print(n + 0, 0 + n, n * 1, 1 * n, n - 0, n ** 1, n / 1, n | 0, n & -1, n << 0, n * 0, 0 * n)
print(1.5 + 0, "a" * 1, [1] * 1 is None, 2 ** 3 ** 2, 1 + 2 * 3, (1, 2) + (3,), "a" + "b", 10 ** 30 * 10 ** 30, 2 ** 64 + 1, 9007199254740993 * 3, 3000000000 * 4000000000, 1 / 3, 7 % 2.5, -7 % 2.5, 1e308 * 10, 5 // 0.3, -5 // 0.3)
def outer(x):
    def g():
        return lambda a=x, *, b=x + 1: (a, b)
    return g()
print(outer(1)(), (lambda *a, k=1, **kw: (a, k, kw))(1, k=2, z=3))
if [1] and True:
    print("a")
print(not not [1], [1] and True, [] or False, True and [1], False or [], 0 and [1], 1 or [1], None or 0, "x" and True, "" and True)
while [] and True:
    pass
sum = 5
print(sum, abs(-1), max([1]), min(1, 2), str(1), int("1"), list((1,)), tuple([1]), range(1), isinstance(1, int))
abs = lambda v: "mine"
print(abs(-1))
