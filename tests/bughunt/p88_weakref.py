# Q4 acceptance. Becomes tests/bughunt/p88_weakref.py when it passes.
# Everything is observed after gc.collect(). The cycle path is deliberately absent: there CPython
# fires callbacks before __del__ (Modules/gcmodule.c:1284 then :1290); SharpPy cannot tell a cycle
# from a refcount drop and follows the refcount order. That is a recorded room B gap.
import gc
import weakref

log = []


class A:
    pass


# ref(), liveness, callback argument.
a = A()
r = weakref.ref(a, lambda ref: log.append(("callback", ref is r, ref())))
print("alive:", r() is a, weakref.getweakrefcount(a))
del a
gc.collect()
print("dead:", r(), log)
log.clear()

# Dead ref hash and equality.
a = A()
r1 = weakref.ref(a)
r2 = weakref.ref(a)
print("same ref object:", r1 is r2, r1 == r2, hash(r1) == hash(a))
d = A()
live_hash = weakref.ref(d)
hash(live_hash)
del d, a, r1, r2
gc.collect()
print("hash survives after first hash:", isinstance(hash(live_hash), int))
d = A()
never_hashed = weakref.ref(d)
del d
gc.collect()
try:
    hash(never_hashed)
except TypeError as e:
    print("TypeError:", e)


# __del__ runs before the weakrefs are cleared: refcount path, Objects/typeobject.c:1964 then :1980.
class WithDel:
    def __del__(self):
        log.append(("__del__", wr() is self))


o = WithDel()
wr = weakref.ref(o, lambda ref: log.append("callback"))
del o
gc.collect()
print("order:", log)
log.clear()

# Resurrection keeps the ref alive and holds the callback back.
keep = []


class Res:
    def __del__(self):
        keep.append(self)


o = Res()
wr = weakref.ref(o, lambda ref: log.append("callback"))
del o
gc.collect()
print("resurrected:", wr() is keep[0], log)
keep.clear()
gc.collect()
print("really gone:", wr(), log)
log.clear()

# Several callbacks on one target, and the order they run in.
o = A()
w1 = weakref.ref(o, lambda ref: log.append("first registered"))
w2 = weakref.ref(o, lambda ref: log.append("second registered"))
w3 = weakref.ref(o, lambda ref: log.append("third registered"))
print("count:", weakref.getweakrefcount(o), len(weakref.getweakrefs(o)))
del o
gc.collect()
print("callback order:", log)
log.clear()

# A ref that dies first never calls back.
o = A()
wr = weakref.ref(o, lambda ref: log.append("must not run"))
del wr
del o
gc.collect()
print("ref died first:", log)

# Exceptions in a callback are reported, not raised.
o = A()
wr = weakref.ref(o, lambda ref: 1 / 0)
del o
gc.collect()
print("after raising callback")

# What refuses a weak reference.
for value in (1, "s", 1.5, (1,), [1], {"a": 1}, None, b"b"):
    try:
        weakref.ref(value)
        print("ref ok:", type(value).__name__)
    except TypeError as e:
        print("TypeError:", e)


class Slotted:
    __slots__ = ("x",)


class SlottedWeak:
    __slots__ = ("x", "__weakref__")


class ListSub(list):
    pass


for value in (Slotted(), SlottedWeak(), ListSub(), set(), frozenset(), lambda: 0, len, A, A()):
    try:
        weakref.ref(value)
        print("ref ok:", type(value).__name__)
    except TypeError as e:
        print("TypeError:", e)

# WeakValueDictionary, WeakKeyDictionary, WeakSet.
wvd = weakref.WeakValueDictionary()
k = A()
wvd["k"] = k
print("wvd:", len(wvd), "k" in wvd)
del k
gc.collect()
print("wvd after:", len(wvd), "k" in wvd)

wkd = weakref.WeakKeyDictionary()
k = A()
wkd[k] = 1
print("wkd:", len(wkd))
del k
gc.collect()
print("wkd after:", len(wkd))

ws = weakref.WeakSet()
k = A()
ws.add(k)
print("ws:", len(ws))
del k
gc.collect()
print("ws after:", len(ws))


# Bound methods.
class M:
    def m(self):
        return 1


o = M()
wm = weakref.WeakMethod(o.m)
print("WeakMethod:", wm()())
del o
gc.collect()
print("WeakMethod after:", wm())

# weakref.finalize.
o = A()
f = weakref.finalize(o, log.append, "finalized")
print("finalize alive:", f.alive)
del o
gc.collect()
print("finalize after:", f.alive, log)
log.clear()
k = A()
weakref.finalize(k, print, "finalize at exit")
print("end of module")
