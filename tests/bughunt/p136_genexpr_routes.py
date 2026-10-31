calls = []
def src(x):
    calls.append(x)
    return range(x)
print(list((x, y) for x in range(4) if x % 2 for y in src(x)))
print(calls)
print(list(x + y for x in range(3) for y in range(3) if x != y if y > 0))
print(list(a + b for a, b in [(1, 2), (3, 4)]))
print(list(a + sum(r) for a, *r in [(1, 2, 3), (4, 5)]))
fs = list((lambda: x) for x in range(3))
print([f() for f in fs])
fs = list((lambda v=x: v) for x in range(3))
print([f() for f in fs])
def outer(n):
    return list(x * n for x in range(3))
print(outer(2))
def nested(n):
    return [list(y + n for y in range(x)) for x in range(3)]
print(nested(1))
print(sum(sum(y for y in range(x)) for x in range(5)))
class K:
    v = [1, 2]
    g = list(x * 2 for x in v)
print(K.g)
g = (x for x in range(2))
print(next(g), next(g), next(g, "end"))
print(g.gi_code.co_name, g.gi_code.co_varnames, bool(g.gi_code.co_flags & 0x20))
def gen_send():
    g = (x for x in [1, 2])
    print(g.send(None))
    try:
        g.send(5)
        print(next(g, "z"))
    except StopIteration:
        print("stop")
gen_send()
d = {"a": 1}
print(list(k + str(d[k]) for k in d))
print(dict((i, i * i) for i in range(3)))
print(any(x > 1 for x in [1, 2]), all(x for x in [1, 0]))
try:
    list(1 / x for x in [1, 0])
except ZeroDivisionError as e:
    print(e)
class It:
    def __init__(self): self.i = 0
    def __iter__(self): return self
    def __next__(self):
        self.i += 1
        if self.i > 2: raise StopIteration
        return self.i
print(list((a, b) for a in It() for b in It()))
print(list(s.obj for s in [type("T", (), {"obj": 3})]))
print(list(x if x else -1 for x in [0, 1]))
w = [1, 2]
print(list((y := x) + 1 for x in w), y)
print([a + sum(r) for a, *r in [(1, 2, 3), (4, 5)]])
print({a: r for a, *r in [(1, 2, 3)]}, {a for *r, a in [(1, 2)]})
print([b for [a, b] in [(1, 2)]], [c for a, (b, [c]) in [(1, (2, [3]))]])
print([(x, y) for (x, y) in [(1, 2)]], [x for (x,) in [(5,)]], [x for ((x, y),) in [((1, 2),)]])
print(list(b for [a, b] in [(1, 2)]), list(x for ((x, y),) in [((1, 2),)]))
class O: pass
o = O()
print([o.v for o.v in range(2)], o.v)
d = {}
print([d["k"] for d["k"] in range(2)], d)
print(list(o.w for o.w in range(2)), o.w)
def f(n):
    fs = [lambda: z + n for z, in [(1,), (2,)]]
    return [g() for g in fs]
print(f(1))
def h():
    return [[(lambda: (a, b)) for b in range(1)] for a in range(1)][0][0]()
print(h())
