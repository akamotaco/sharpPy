# zip, map, filter, enumerate, reversed: iterators that pull one item at a time.
class Ints:
    def __init__(self, limit=None):
        self.n = 0
        self.limit = limit
        self.pulled = 0

    def __iter__(self):
        return self

    def __next__(self):
        self.pulled += 1
        if self.limit is not None and self.n >= self.limit:
            raise StopIteration
        self.n += 1
        return self.n - 1


a, b = Ints(), Ints(2)
z = zip(a, b)
print(a.pulled, b.pulled, type(z).__name__, iter(z) is z)
print(next(z), a.pulled, b.pulled)
print(list(z), a.pulled, b.pulled)
print(list(z), a.pulled, b.pulled)

a, b = Ints(2), Ints()
print(list(zip(a, b)), a.pulled, b.pulled)
a = Ints()
m = map(lambda x: x * 2, a)
print(a.pulled, next(m), next(m), a.pulled)
a = Ints()
f = filter(lambda x: x % 3 == 2, a)
print(a.pulled, next(f), a.pulled, next(f), a.pulled)
a = Ints()
e = enumerate(a, 5)
print(a.pulled, next(e), a.pulled)
a = Ints()
m = map(lambda x, y: (x, y), a, "ab")
print(list(m), a.pulled)
print(next(zip(Ints(), Ints(), Ints())))
try:
    zip(1, 2)
except TypeError as e:
    print(e)
try:
    zip([], 42)
except TypeError as e:
    print(e)
try:
    map(len, 42)
except TypeError as e:
    print(e)
try:
    filter(None, 42)
except TypeError as e:
    print(e)
try:
    list(zip([1, 2], [1], strict=True))
except ValueError as e:
    print(e)
try:
    list(zip([1], [1, 2], strict=True))
except ValueError as e:
    print(e)
try:
    list(zip([1], [1], [1, 2], strict=True))
except ValueError as e:
    print(e)
print(list(zip()), list(zip([1], strict=True)), list(zip(*[(1, 2), "ab"])))
print(type(map(len, [])).__name__, type(filter(None, [])).__name__, type(enumerate([])).__name__, type(reversed([])).__name__)


def gen():
    yield 1
    raise ValueError("late")


z = zip(gen(), Ints())
print(next(z))
try:
    next(z)
except ValueError as e:
    print(e)
d = {1: 2}
it = zip(d, d)
print(next(it))
items = [1, 2, 3]
m = map(str, items)
items.append(4)
print(list(m))
r = reversed(items)
items.pop()
print(list(r))
print(sum(x for x, _ in zip(Ints(), range(100))), max(map(abs, [-3, 2])), any(map(bool, Ints())), all(map(bool, Ints())))
print(tuple(zip("ab", Ints())), dict(zip("ab", Ints())), set(map(abs, [1, -1])), sorted(filter(None, [0, 2, 1])))
print(*zip("ab", "cd"), *map(str, (1, 2)))
print(next(enumerate("a", 2 ** 70)), next(enumerate("a", True)), next(enumerate("a", start=3)), list(enumerate(iterable="a")))


class Index:
    def __index__(self):
        return 7


print(next(enumerate("a", Index())))
for bad in ("x", 1.5, None):
    try:
        enumerate("a", bad)
    except TypeError as e:
        print(e)
print(list(zip()), next(enumerate(Ints())), list(enumerate(iter([1]))))
