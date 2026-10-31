# P0-52. A Python call takes a frame, not host stack, so the limit decides, and every overflow is a
# RecursionError that can be caught. Depths are measured against the default limit of 1000.
import sys


def down(n):
    return 0 if n == 0 else 1 + down(n - 1)


print(sys.getrecursionlimit())
for depth in (50, 100, 500, 900, 990):
    print(depth, down(depth))


def forever(n):
    return forever(n + 1)


try:
    forever(0)
except RecursionError as e:
    print(type(e).__name__, e)

deepest = 0


def measure(n):
    global deepest
    deepest = n
    measure(n + 1)


try:
    measure(1)
except RecursionError:
    print("deepest", deepest)


class Node:
    def __init__(self, nxt):
        self.nxt = nxt

    def depth(self):
        return 1 if self.nxt is None else 1 + self.nxt.depth()


n = None
for i in range(900):
    n = Node(n)
print(n.depth())


def outer():
    def rec(k):
        return k if k == 0 else rec(k - 1)

    return rec(900)


print(outer())


def with_try(k):
    try:
        return with_try(k + 1)
    except RecursionError:
        return k


print(with_try(0) > 900)


def finally_order(k, log):
    try:
        if k == 3:
            raise ValueError("deep")
        finally_order(k + 1, log)
    finally:
        log.append(k)


log = []
try:
    finally_order(0, log)
except ValueError as e:
    tb = e.__traceback__
    count = 0
    while tb:
        count += 1
        tb = tb.tb_next
    print(log, count)


def caught_above(k):
    if k == 0:
        raise KeyError("x")
    return caught_above(k - 1)


def guard():
    try:
        caught_above(200)
    except KeyError as e:
        return repr(e), sys.exc_info()[0].__name__


print(guard(), sys.exc_info())


class R:
    def __repr__(self):
        return repr(self)


try:
    repr(R())
except RecursionError as e:
    print("repr", type(e).__name__)


class G:
    def __getattr__(self, name):
        return getattr(self, name + "x")


try:
    G().a
except RecursionError as e:
    print("getattr", type(e).__name__)


def gen(k):
    yield from gen(k + 1)


try:
    list(gen(0))
except RecursionError as e:
    print("generator", type(e).__name__)


l = []
l.append(l)
print(repr(l))
d = []
for i in range(50):
    d = [d]
print(len(repr(d)))
sys.setrecursionlimit(50)
try:
    down(60)
except RecursionError as e:
    print("lowered", e)
sys.setrecursionlimit(1000)
print(down(100))


def kw(k=0, *, stop=500):
    return k if k == stop else kw(k=k + 1, stop=stop)


print(kw())
print(sorted([3, 1, 2], key=lambda v: down(v)), [down(v) for v in range(3)], list(map(down, (1, 2))))
