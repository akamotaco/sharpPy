import copy
import copyreg


class Entity:
    def __init__(self, name, items):
        self.name = name
        self.items = items

    def __repr__(self):
        return f"Entity({self.name!r}, {self.items!r})"


e = Entity("a", [{"k": [1, 2]}])
s = copy.copy(e)
d = copy.deepcopy(e)
e.items[0]["k"].append(3)
print(s, d, s.items is e.items, d.items is e.items, type(d) is Entity)


class Slotted:
    __slots__ = ("x", "y")

    def __init__(self):
        self.x = [1]


a = Slotted()
b = copy.deepcopy(a)
c = copy.copy(a)
print(b.x, b.x is a.x, c.x is a.x, hasattr(b, "y"))


class Both(Entity):
    __slots__ = ("z",)


q = Both("n", [])
q.z = [5]
r = copy.deepcopy(q)
print(r, r.z, r.z is q.z)


class WithState:
    def __init__(self):
        self.a = 1
        self.b = 2

    def __getstate__(self):
        return {"a": self.a}

    def __setstate__(self, state):
        self.a = state["a"] * 10


w = copy.deepcopy(WithState())
print(w.a, hasattr(w, "b"), copy.copy(WithState()).a)


class NewArgs:
    def __new__(cls, v):
        o = super().__new__(cls)
        o.v = v
        return o

    def __getnewargs__(self):
        return (self.v,)


print(copy.copy(NewArgs(3)).v, copy.deepcopy(NewArgs(4)).v)


class MyList(list):
    def __init__(self, *a):
        super().__init__(*a)
        self.tag = "t"


class MyDict(dict):
    pass


ml = MyList([1, [2]])
md = MyDict(a=[1])
md.flag = True
ml2 = copy.deepcopy(ml)
md2 = copy.deepcopy(md)
print(ml2, type(ml2).__name__, ml2.tag, ml2[1] is ml[1], md2, type(md2).__name__, md2.flag, md2["a"] is md["a"])
print(copy.copy(ml), copy.copy(md), type(copy.copy(ml)).__name__)


class Cycle:
    pass


x = Cycle()
x.me = x
y = copy.deepcopy(x)
print(y.me is y, y is not x)
lst = []
lst.append(lst)
print(copy.deepcopy(lst)[0] is not lst)


class Custom:
    def __init__(self):
        self.calls = []

    def __deepcopy__(self, memo):
        c = Custom()
        c.calls = ["deep"]
        return c

    def __copy__(self):
        c = Custom()
        c.calls = ["shallow"]
        return c


print(copy.deepcopy(Custom()).calls, copy.copy(Custom()).calls)
print(copy.deepcopy([1, (2, [3])]), copy.deepcopy({"a": {1, 2}}), copy.copy((1, 2)), copy.deepcopy(None), copy.deepcopy(len))
print(Entity("a", 1).__reduce_ex__(2)[0] is copyreg.__newobj__, Entity("a", 1).__reduce_ex__(2)[1] == (Entity,))
print(Entity("a", 1).__reduce_ex__(2)[2], Entity("a", 1).__getstate__(), object().__getstate__(), Slotted().__getstate__())
print(Entity("a", 1).__reduce__()[0] is copyreg._reconstructor)
