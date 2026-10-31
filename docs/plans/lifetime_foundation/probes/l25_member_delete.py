# L2.5 route: deletion takes the same MRO lookup as assignment (PyObject_GenericSetAttr with NULL). L2.4
# taught SetAttributeDefault about builtin MRO levels and left the delete route behind, so `del e.code`
# on a SystemExit subclass stopped working.
class S(SystemExit):
    pass


class H(SystemExit):
    def __setattr__(self, n, v):
        print("set", n)
        super().__setattr__(n, v)

    def __delattr__(self, n):
        print("del", n)
        super().__delattr__(n)


for k in (S, SystemExit, H):
    e = k(3)
    del e.code
    print(k.__name__, e.code, e.args)
    del e.code
    print(k.__name__, e.code, e.args)

h = H(1)
h.other = 3
del h.other
print(h.__dict__)

try:
    del S(1).missing
except AttributeError as x:
    print(x)


class E(Exception):
    pass


for k in (E, ValueError, S, SystemExit):
    e = k("a")
    try:
        del e.args
    except TypeError as x:
        print(k.__name__, x)
    print(e.args)
