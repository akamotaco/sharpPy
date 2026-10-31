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
