def show(label, fn):
    try:
        print(f"{label!r}: {fn()!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e}")

class Base:
    def method(self):
        return "base"
class Mid(Base):
    def method(self):
        return "mid+" + super().method()
class Leaf(Mid):
    pass
show("super.thru.mro", lambda: Leaf().method())


class Props:
    @property
    def x(self):
        return "get"
show("prop.inherited", lambda: type("P2", (Props,), {})().x)
show("prop.no.setter", lambda: (lambda p: (setattr(p, "x", 1), p.x)[1])(Props()))
def prop_del():
    p = Props()
    del p.x
    return p.x
show("prop.no.deleter", prop_del)

class Slotted:
    __slots__ = ("a",)
show("slots.get.set", lambda: (lambda o: (setattr(o, "a", 1), o.a)[1])(Slotted()))
show("slots.missing", lambda: Slotted().b)
show("slots.no.dict", lambda: hasattr(Slotted(), "__dict__"))
class SubSlotted(Slotted):
    pass
show("slots.subclass.dict", lambda: (lambda o: (setattr(o, "z", 9), o.z)[1])(SubSlotted()))
class DupSlots(Slotted):
    __slots__ = ("a", "b")
show("slots.dup.field", lambda: (lambda o: (setattr(o, "a", 1), o.a)[1])(DupSlots()))

class GD:
    data = "cls"
    def __getattr__(self, name):
        return "dyn:" + name
show("getattr.fallback", lambda: [GD().data, GD().missing])
show("getattr.on.class.attr", lambda: getattr(GD(), "data"))

class GA:
    def __getattribute__(self, name):
        return "all:" + name
show("getattribute.every.time", lambda: GA().whatever)

class Meta(type):
    def __new__(mcs, name, bases, ns):
        ns["tag"] = "meta"
        return super().__new__(mcs, name, bases, ns)
class WithMeta(metaclass=Meta):
    pass
show("metaclass.new", lambda: WithMeta.tag)

class SubKwargs:
    def __init_subclass__(cls, flavor="plain", **kw):
        super().__init_subclass__(**kw)
        cls.flavor = flavor
class Made(SubKwargs, flavor="vanilla"):
    pass
show("init_subclass.kw", lambda: Made.flavor)

class Desc:
    def __set_name__(self, owner, name):
        self.name = name
    def __get__(self, obj, objtype=None):
        return f"desc({self.name},{obj is None})"
class HasDesc:
    d = Desc()
show("set.name.called", lambda: [HasDesc.d, HasDesc().d])

class DataDesc:
    def __get__(self, obj, t=None): return "data"
    def __set__(self, obj, v): pass
class InstanceWins:
    dd = DataDesc()
show("data.desc.wins.over.instance", lambda: (lambda o: (setattr(o, "dd", "inst"), o.dd)[1])(InstanceWins()))
class NonData:
    def __get__(self, obj, t=None): return "nond"
class NDWins:
    nd = NonData()
show("instance.wins.over.nondict", lambda: (lambda o: (setattr(o, "nd", "inst"), o.nd)[1])(NDWins()))

class StaticChained:
    @staticmethod
    def f():
        return 1
show("staticmethod.thru.instance", lambda: StaticChained().f())
show("staticmethod.thru.class", lambda: StaticChained.f())

class ClsM:
    @classmethod
    def who(cls):
        return cls.__name__
class ClsM2(ClsM):
    pass
show("classmethod.bound.sub", lambda: ClsM2.who())

def make_init_sub_err():
    class InitSubErr(SubKwargs, bad=1):
        pass
    return InitSubErr.flavor
show("init_subclass.kw.consume", make_init_sub_err)

class EqNe:
    def __eq__(self, other): return True
show("ne.auto.from.eq", lambda: [(EqNe() != 5), (EqNe() == 5)])

class BothNotImpl:
    def __eq__(self, other): return NotImplemented
show("eq.both.notimpl", lambda: [BothNotImpl() == BothNotImpl(), BothNotImpl() != BothNotImpl(), BothNotImpl() is BothNotImpl()])
show("is.dupe", lambda: (lambda x: x is x)(BothNotImpl()))

class BoolLen:
    def __bool__(self): return False
    def __len__(self): return 5
show("bool.beats.len", lambda: bool(BoolLen()))
class LenOnly:
    def __len__(self): return 0
show("len.zero.false", lambda: bool(LenOnly()))

class CtxErr:
    def __enter__(self): return self
    def __exit__(self, t, v, tb): return True
with CtxErr() as c:
    raise ValueError("swallowed?")
print("with.swallowed: yes")

class CtxOk:
    def __enter__(self):
        print("  enter")
        return "obj"
    def __exit__(self, *a):
        print("  exit", a[0].__name__ if a[0] else None)
        return False
with CtxOk() as o:
    print("  body", o)

class SlotsEq:
    __slots__ = ("v",)
    def __init__(self, v): self.v = v
show("no.eq.identity", lambda: SlotsEq(1) == SlotsEq(1))

# last: in CPython duplicate bases raise TypeError, but in SharpPy a raw CLR
# exception leaks past `except BaseException`, so this probe must run last
show("mro.duplicate", lambda: type("C", (Base, Base), {}))
print("p08-tail-reached")
