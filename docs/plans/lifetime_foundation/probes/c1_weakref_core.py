# C1 acceptance, written before the core exists. Uses _weakref alone, so it can pass before
# Lib/weakref.py is installed. Every observation is after gc.collect(). The cycle path stays out for
# the reason given at the top of q4_weakref.py.
import _weakref
import gc

log = []


def settle():
    gc.collect()
    gc.collect()


class A:
    pass


# Liveness, callback argument, the ref is dead by the time its callback runs.
a = A()
r = _weakref.ref(a, lambda ref: log.append((ref is r, ref())))
print("alive:", r() is a, _weakref.getweakrefcount(a))
del a
settle()
print("dead:", r(), log)
log.clear()


# __del__ runs first and still sees the ref alive; the callback comes after it.
class WithDel:
    def __del__(self):
        log.append(("__del__", wr() is self))


o = WithDel()
wr = _weakref.ref(o, lambda ref: log.append(("callback", ref())))
del o
settle()
print("order:", log)
log.clear()

# Resurrection keeps the ref alive and holds the callback back until the real death.
keep = []


class Res:
    def __del__(self):
        keep.append(self)


o = Res()
wr = _weakref.ref(o, lambda ref: log.append("callback"))
del o
settle()
print("resurrected:", wr() is keep[0], log)
keep.clear()
settle()
print("really gone:", wr(), log)
log.clear()

# A ref made during the object's second life is served by the same death.
o = Res()
del o
settle()
late = _weakref.ref(keep[0], lambda ref: log.append("late callback"))
keep.clear()
settle()
print("ref made after resurrection:", late(), log)
log.clear()

# Newest first.
o = A()
w1 = _weakref.ref(o, lambda ref: log.append("first"))
w2 = _weakref.ref(o, lambda ref: log.append("second"))
w3 = _weakref.ref(o, lambda ref: log.append("third"))
print("count:", _weakref.getweakrefcount(o), len(_weakref.getweakrefs(o)))
del o
settle()
print("callback order:", log)
log.clear()

# A ref that dies first never calls back, and it leaves the count.
o = A()
wr = _weakref.ref(o, lambda ref: log.append("must not run"))
del wr
settle()
print("count after ref died:", _weakref.getweakrefcount(o))
del o
settle()
print("ref died first:", log)

# A ref reachable only through its own target still calls back.
o = A()
o.r = _weakref.ref(o, lambda ref: log.append("owned callback"))
del o
settle()
print("ref owned by target:", log)
log.clear()


# A child's ref read from the parent's __del__: the parent still owns the child there.
class Parent:
    def __init__(self):
        self.child = A()
        self.child_ref = _weakref.ref(self.child)

    def __del__(self):
        log.append(self.child_ref() is self.child)


o = Parent()
del o
settle()
print("child in parent __del__:", log)
log.clear()


# A generator that finished, then got a weakref: nothing is owed to close(), the ref is still owed.
def g():
    yield 1


i = g()
list(i)
wr = _weakref.ref(i, lambda ref: log.append("generator callback"))
del i
settle()
print("finished generator:", wr(), log)
log.clear()

# Suspended generator: the opposite of __del__. gen_dealloc clears the weakrefs before it finalizes,
# so the callback runs first and the finally second.
def h():
    try:
        yield 1
    finally:
        log.append("finally")


i = h()
next(i)
wr = _weakref.ref(i, lambda ref: log.append("callback"))
del i
settle()
print("suspended generator:", wr(), log)
log.clear()


# A subclass is a real type with its own slots, which is what Lib/weakref.py KeyedRef needs. A plain
# ref takes no attributes, and only the plain callback-less ref is shared.
class Keyed(_weakref.ref):
    __slots__ = ("key",)

    def __new__(cls, target, callback, key):
        self = _weakref.ref.__new__(cls, target, callback)
        self.key = key
        return self

    def __init__(self, target, callback, key):
        super().__init__(target, callback)


o = A()
k = Keyed(o, lambda ref: log.append(("keyed", ref.key)), "the key")
print("subclass:", type(k).__name__, isinstance(k, _weakref.ref), k.key, k() is o)
print("not shared:", _weakref.ref(o) is k, Keyed(o, None, 1) is Keyed(o, None, 1))
plain = _weakref.ref(o)
print("shared:", plain is _weakref.ref(o), plain.__callback__)
try:
    plain.key = 1
except AttributeError as e:
    print("AttributeError:", e)
del o
settle()
print("keyed death:", k(), log)
log.clear()


# Handing the object out must never disagree with what happens to it afterwards. On CPython the
# object is gone at `del`; on SharpPy the collection can land between the two lines below, so this
# loop is what catches a ref that gives out a condemned object.
wrong = 0
for round_number in range(20000):
    o = A()
    wr = _weakref.ref(o)
    del o
    junk = [[] for j in range(50)]
    held = wr()
    if held is not None:
        junk = [[] for j in range(50)]
        if wr() is not held:
            wrong += 1
    del held
settle()
print("disagreements:", wrong)

# Exceptions in a callback are reported, not raised.
o = A()
wr = _weakref.ref(o, lambda ref: 1 / 0)
del o
settle()
print("after raising callback")
