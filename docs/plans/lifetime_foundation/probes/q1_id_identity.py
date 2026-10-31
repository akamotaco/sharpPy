# Q1 acceptance. CPython: id() is the address (Python/bltinmodule.c:1234 builtin_id), so it is unique
# among simultaneously live objects and agrees with `is`. Output must be identical on both.
import gc

a = "hello world"
b = "".join(["hello", " world"])
print("equal str   is/id:", a is b, id(a) == id(b))

x = 1000000
y = int("1000000")
print("equal int   is/id:", x is y, id(x) == id(y))

f1 = 1.5
f2 = float("1.5")
print("equal float is/id:", f1 is f2, id(f1) == id(f2))


class H:
    def __hash__(self):
        return 7

    def __eq__(self, other):
        return True


h1, h2 = H(), H()
print("user hash   is/id:", h1 is h2, id(h1) == id(h2))

# Uniqueness among live objects. The numbers are the failure measured on 2026-10-31:
# SharpPy produced 299312 distinct ids here.
lists = [[] for i in range(300000)]
print("distinct ids of live lists:", len(set(id(o) for o in lists)))


# The shape of copy.deepcopy's memo (Lib/copy.py). A collision here silently returns a wrong object
# once copy.py is available. Measured: 667 collisions.
class N:
    pass


ns = [N() for i in range(300000)]
memo = {}
collisions = 0
for o in ns:
    if id(o) in memo:
        collisions += 1
    memo[id(o)] = o
print("memo collisions:", collisions)

# Stability: a value that survives collections and is the same for one object every time.
o = object()
before = id(o)
gc.collect()
junk = [object() for i in range(100000)]
del junk
gc.collect()
print("stable across gc:", id(o) == before, id(o) == id(o))
print("id type:", type(id(o)).__name__, id(o) > 0)

# Immutable singletons keep one identity.
print("singletons:", id(None) == id(None), id(True) == id(True), id(()) == id(()))
