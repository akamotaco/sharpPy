# L2.5 route: obj.__dict__ is the object's one attribute store, not a view that stops seeing later
# writes (PyObject_GenericGetDict hands back the dict the attribute writes go to).
class P:
    pass


o = P()
print(o.__dict__)
o.x = 1
print(o.__dict__)
d = o.__dict__
o.y = 2
print(d, len(d), sorted(d), "y" in d, d.get("y"))
d["z"] = 3
print(o.z, d is o.__dict__, vars(o) is d)
del o.x
print(d, hasattr(o, "x"))
del d["y"]
print(hasattr(o, "y"), d)
print(list(d.items()), dict(d), repr(d))


class K:
    def __init__(self):
        self.a = 1
        self.__dict__["b"] = 2
        self.c = 3


k = K()
print(k.__dict__, k.a, k.b, k.c)
print(vars(k))
k.d = 4
print(vars(k))
k.__dict__.update(e=5)
print(k.e, k.__dict__)
k.__dict__.clear()
print(hasattr(k, "a"), k.__dict__)
k.f = 6
print(k.__dict__)


class E(Exception):
    pass


e = E(1)
print(e.__dict__)
e.a = 1
print(e.__dict__)
e.__dict__["b"] = 2
print(e.b, e.args)


p = P()
p.a = 1
print(p.__dict__)
d2 = {"b": 2}
p.__dict__ = d2
print(p.b, hasattr(p, "a"), p.__dict__ is d2)
d2["c"] = 3
p.e = 4
print(p.c, d2)
try:
    p.__dict__ = 5
except TypeError as x:
    print(x)
p.__dict__[1] = "int key"
print(p.__dict__, vars(p))
print(p.e)
q = P()
q.x = 1
q.y = 2
del q.x
q.z = 3
print(list(q.__dict__))
