# Q2 acceptance. Every observation happens AFTER gc.collect(): that is the deterministic anchor.
# Do not "fix" a difference by printing earlier; the moment before gc.collect() is room B.
import gc
import sys

log = []


def show(title):
    gc.collect()
    print(title, log)
    log.clear()


# A. Delivery: nothing is lost. Measured failure: 7887 of 300000.
count = [0]


class D:
    def __del__(self):
        count[0] += 1


def churn():
    for i in range(300000):
        D()


churn()
gc.collect()
print("A delivered:", count[0])


# B. A local object dropped at return.
class B:
    def __del__(self):
        log.append("B.__del__")


def scope():
    b = B()


scope()
show("B local:")


# C. Resurrection: __del__ runs once (Objects/object.c:364), the object stays usable.
saved = []


class R:
    calls = 0

    def __del__(self):
        R.calls += 1
        saved.append(self)


class S:
    __slots__ = ()
    calls = 0

    def __del__(self):
        S.calls += 1
        saved.append(self)


for C in (R, S):
    x = C()
    del x
    gc.collect()
    print("C", C.__name__, "first:", C.calls, len(saved), type(saved[0]).__name__)
    x = saved.pop()
    del x
    gc.collect()
    print("C", C.__name__, "second:", C.calls, len(saved))


# D. Type lookup: a subclass override wins, super() reaches the base (slot_tp_finalize).
class Base:
    def __del__(self):
        log.append("Base.__del__")


class Child(Base):
    def __del__(self):
        log.append("Child.__del__")
        super().__del__()


class Inherit(Base):
    pass


o = Child()
del o
show("D override:")
o = Inherit()
del o
show("D inherited:")


# E. A raising __del__ is reported, never propagated, and the others still run.
class Boom:
    def __del__(self):
        log.append("Boom")
        raise ValueError("boom in del")


o = Boom()
del o
show("E raising:")


# F. self and what it owns are intact inside __del__.
class Owner:
    def __init__(self):
        self.child = [1, 2, 3]

    def __del__(self):
        log.append(sum(self.child))


o = Owner()
del o
show("F state:")


# G. __del__ assigned to the class before the instance exists: the per-class cache of "declares a
# finalizer" must not keep the old answer. The instance-before-assignment case is a recorded gap,
# see q2_gap_late_del.py.
class Late:
    pass


first = Late()
del first
Late.__del__ = lambda self: log.append("Late.__del__")
o = Late()
del o
show("G late-assigned:")


class LateChild(Late):
    pass


o = LateChild()
del o
show("G inherited:")


class LateParent:
    pass


class LateSub(LateParent):
    pass


first = LateSub()
del first
LateParent.__del__ = lambda self: log.append("LateParent.__del__")
o = LateSub()
del o
show("G parent assigned after subclass used:")


# H. Instances that do not come through the class call.
class N:
    def __del__(self):
        log.append("N.__del__")


o = object.__new__(N)
del o
show("H object.__new__:")
o = N.__new__(N)
del o
show("H cls.__new__:")


# I. Builtin-derived instances are ordinary heap-type instances.
class L(list):
    def __del__(self):
        log.append("list subclass")


class Dt(dict):
    def __del__(self):
        log.append("dict subclass")


class St(str):
    def __del__(self):
        log.append("str subclass")


class In(int):
    def __del__(self):
        log.append("int subclass")


class Tp(tuple):
    def __del__(self):
        log.append("tuple subclass")


class Fl(float):
    def __del__(self):
        log.append("float subclass")


class Ex(Exception):
    def __del__(self):
        log.append("exception subclass")


for make in (L, Dt, lambda: St("s"), lambda: In(5), lambda: Tp((1,)), lambda: Fl(1.5), Ex):
    o = make()
    del o
    show("I builtin base:")


# J. The running exception survives a finalizer that raises and handles its own.
class Quiet:
    def __del__(self):
        try:
            raise KeyError("inner")
        except KeyError:
            pass
        log.append("Quiet")


try:
    raise ValueError("outer")
except ValueError:
    o = Quiet()
    del o
    gc.collect()
    print("J exc_info:", sys.exc_info()[0].__name__, str(sys.exc_info()[1]), log)
    log.clear()

# K. gc.disable() stops the cycle collector only; dropping the last reference still finalizes.
gc.disable()
o = B()
del o
gc.collect()
print("K disabled:", log)
log.clear()
gc.enable()


# L. A finalizer that creates finalizable objects.
class Parent:
    def __del__(self):
        log.append("Parent")
        B()


o = Parent()
del o
show("L nested:")


# M. gc.callbacks bracket the finalizers of a cycle.
def cb(phase, info):
    log.append(phase)


class Cyc:
    def __del__(self):
        log.append("Cyc.__del__")


gc.collect()
gc.callbacks.append(cb)
o = Cyc()
o.me = o
del o
gc.collect()
gc.callbacks.remove(cb)
print("M callbacks:", log)
log.clear()


# N. gc.is_finalized.
class Fz:
    def __del__(self):
        saved.append(self)


o = Fz()
print("N before:", gc.is_finalized(o))
del o
gc.collect()
print("N after:", gc.is_finalized(saved[0]))
saved.clear()
gc.collect()


# P. The hook belongs to the object's real type, not to the class that was called.
class Foreign:
    def __new__(cls):
        return []

    def __del__(self):
        log.append("must not run")


o = Foreign()
del o
show("P foreign __new__:")


# O. Objects still alive at exit are finalized after the module ends.
class AtExit:
    def __init__(self, tag):
        self.tag = tag

    def __del__(self):
        print("exit __del__", self.tag)


keep = AtExit("kept")
print("end of module")
