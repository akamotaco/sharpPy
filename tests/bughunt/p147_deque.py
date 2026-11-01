import collections.abc
import copy
from collections import deque

d = deque([1, 2, 3], maxlen=3)
print(d, d.maxlen, len(d), list(d), d[0], d[-1], 2 in d, 9 in d, bool(d), bool(deque()))
d.append(4)
print(d)
d.appendleft(0)
print(d)
d.extend([7, 8])
print(d)
d.extendleft([5, 6])
print(d, d.pop(), d.popleft(), d)
e = deque("abc")
print(e, e.maxlen, e.count("a"), e.index("b"), repr(e), str(e), list(reversed(e)))
e.rotate(1)
print(e)
e.rotate(-2)
print(e)
e.rotate(0)
e.rotate(10)
print(e)
e.insert(1, "z")
print(e)
e.remove("z")
print(e)
e.reverse()
print(e)
e[0] = "q"
del e[1]
print(e, e.copy(), e.copy() is e, e == deque(["q", "a"]), e != deque(), e < deque(["z"]))
e.clear()
print(e, len(e))
for call in (e.pop, e.popleft):
    try:
        call()
    except IndexError as x:
        print(x)
try:
    deque([1]).index(2)
except ValueError as x:
    print(x)
try:
    deque([1]).remove(2)
except ValueError as x:
    print(x)
try:
    deque()[0]
except IndexError as x:
    print(x)
try:
    deque(maxlen=-1)
except ValueError as x:
    print(x)
try:
    deque([1], 2).insert(0, 1) or deque([1], 1).insert(0, 2)
except IndexError as x:
    print(x)
print(deque(maxlen=0), deque([1, 2], maxlen=0), deque(range(5), 2), deque(maxlen=None))
print(deque([1]) + deque([2]), deque([1, 2]) * 2, deque([1, 2], maxlen=3) * 2)
z = deque([1])
z += [2]
z *= 2
print(z, sum(deque([1, 2])), sorted(deque([3, 1])), tuple(deque([1])), [x * 2 for x in deque([1, 2])])
print(copy.copy(deque([1], 2)), copy.deepcopy(deque([[1]], 2)), isinstance(deque(), collections.abc.Sequence))
try:
    hash(deque())
except TypeError:
    print("unhashable")
it = iter(deque([1, 2]))
print(next(it))
q = deque([1, 2])
i = iter(q)
q.append(3)
try:
    next(i)
except RuntimeError as x:
    print(x)


class D(deque):
    def __init__(self, *a):
        super().__init__(*a)
        self.tag = 1

    def first(self):
        return self[0]


s = D([1, 2])
s.append(3)
print(s, s.first(), s.tag, len(s), isinstance(s, deque), type(s.copy()).__name__)
big = deque()
for i in range(200000):
    big.append(i)
    if len(big) > 100:
        big.popleft()
print(len(big), big[0], big[50], big[-1], sum(big))


def m(c):
    match c:
        case [a, *b]:
            return a, b
    return None


print(m(deque([1, 2, 3])))
