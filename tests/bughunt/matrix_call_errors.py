import sys

def show(name, fn):
    try:
        r = fn()
        print(f"{name!r}: {r!r}")
    except Exception as e:
        print(f"{name!r}: EXC {type(e).__name__}: {e}")

def take(a, b, c="cdef", *args, d=4, **kw):
    return (a, b, c, args, d, kw)

def no_defaults(p, q):
    return p + q

def with_defaults(x, y=1, z=2):
    return (x, y, z)

def kwonly_req(a, *, k):
    return (a, k)

def kwonly_def(a, *, k=9):
    return (a, k)

def posonly(a, /, b):
    return (a, b)

def star_only(*, x=1):
    return x

class C:
    def meth(self, m1, m2):
        return (m1, m2)
    @staticmethod
    def stat(s1):
        return s1
    @classmethod
    def cls(clsme, c1):
        return c1

show("m.zero", lambda: take())
show("m.two.missing", lambda: no_defaults(1))
show("m.one.missing", lambda: no_defaults())
show("m.toomany.defaults", lambda: with_defaults(1, 2, 3, 4))
show("m.toomany.star", lambda: with_defaults(*[1,2,3,4]))
show("m.dup.kw.dict", lambda: take(a=1, **{"a": 2}))
show("m.dup.poskw", lambda: no_defaults(1, a=2))
show("m.dup.poskw2", lambda: no_defaults(1, 2, a=3))
show("m.unexpected", lambda: no_defaults(1, 2, zz=3))
show("m.kwonly.missing", lambda: kwonly_req(1))
show("m.kwonly.pos", lambda: kwonly_def(1, 2))
show("m.posonly.kw", lambda: posonly(1, b=2))
show("m.posonly.aspos", lambda: posonly(1, 2))
show("m.none.kw", lambda: no_defaults(**{"p": 1, "q": 2}))
show("m.order.star.const", lambda: take(*[1], 2))
show("m.order.const.star", lambda: take(1, *[2]))
show("m.order.two.star", lambda: take(*[1], *[2], 3))
show("m.kwex", lambda: take("x", *[ "y" ], **{"d": "D"}))
show("m.kwex.only", lambda: take(**{"a": 1, "b": 2}))
show("m.kwex.dup", lambda: take("A", **{"a": 1, "b": 2}))
show("m.method.ok", lambda: C().meth(1, 2))
show("m.method.missing", lambda: C().meth(1))
show("m.method.toomany", lambda: C().meth(1, 2, 3))
show("m.stat.missing", lambda: C.stat())
show("m.cls.missing", lambda: C.cls())
lam_named = lambda lz1, lz2: lz1
show("m.lambda.missing", lambda: lam_named(1))
show("m.lambda.toomany", lambda: lam_named(1, 2, 3))
nested_holder = []
def outer():
    def inner(i1, i2):
        return (i1, i2)
    return inner
show("m.nested.missing", lambda: outer()())
show("m.call.int", lambda: no_defaults.__call__(5))
bad_kw_type = {}
try:
    no_defaults(**{1: 2})
except Exception as e:
    print("m.keywords.notstr:", type(e).__name__ + ":", e)
show("m.keywords.nonstr2", lambda: no_defaults(1, **{1: 2}))
show("m.star.noniter", lambda: take(*5))
show("m.kwex.nonmap", lambda: take(*[1, 2], 3))
