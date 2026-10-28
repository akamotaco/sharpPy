def show(label, fn):
    try:
        print(repr(label) + ":", repr(fn()))
    except Exception as e:
        print(repr(label) + ":", type(e).__name__ + ":", e)

def mk():
    return {"a": 1, "b": 2}

class D(dict): pass

class M:
    def keys(self): return ["x", "y"]
    def __getitem__(self, k): return k * 2

d = mk(); d.__ior__({"a": 2})
show("ior.ret", lambda: (lambda d: (d.__ior__({"a": 2}), d, d.__ior__ is not None)[1])(mk()))
show("ior.op", lambda: (lambda d: (d.__ior__({"a": 2}), d)[1])(mk()))
show("ior.bad", lambda: (lambda d: d.__ior__(5))(mk()))
show("ior.op5", lambda: (lambda d: d.__ior__(5))(mk()))
show("or.new", lambda: mk() | {"c": 3})
show("or.bad", lambda: mk() | 5)
show("ror.d", lambda: 5 | mk())
show("and.k", lambda: {"a": 1, "b": 2} & {"a": 9, "c": 3})
show("and.bad", lambda: {"a": 1} & ["a"])
show("sub.k", lambda: {"a": 1, "b": 2} - {"a": 9})
show("xor.k", lambda: {"a": 1, "b": 2} ^ {"a": 9, "c": 3})
show("ior.ident", lambda: (lambda d: d.__ior__({}) is d)(mk()))
show("eq.unbound", lambda: dict.__eq__({}, D()) and True)
show("eq.sub", lambda: {} == D())
show("ne.unbound", lambda: dict.__ne__({"a": 1}, {"a": 1}))
show("eq.dups", lambda: dict.__eq__({"a": 1}, D(a=1)))
show("upd.pairs", lambda: (lambda d: (d.update([("c", 3)]), d)[1])(mk()))
show("upd.kw", lambda: (lambda d: (d.update(c=3), d)[1])(mk()))
show("upd.both", lambda: (lambda d: (d.update([("c", 3)], d=4), d)[1])(mk()))
show("upd.dictsub", lambda: (lambda d: (d.update(D(c=3)), d)[1])(mk()))
show("upd.mapping", lambda: (lambda d: (d.update(M()), d)[1])(mk()))
show("upd.str2", lambda: (lambda d: (d.update(["ab"]), d)[1])(mk()))
show("upd.elem1", lambda: (lambda d: d.update([1]))(mk()))
show("upd.elem3", lambda: (lambda d: d.update([(1, 2, 3)]))(mk()))
show("upd.int", lambda: (lambda d: d.update(5))(mk()))
show("upd.two", lambda: (lambda d: d.update({}, {}))(mk()))
show("upd.over", lambda: (lambda d: (d.update({"a": 9}, a=8), d)[1])(mk()))
# live views
show("view.len", lambda: (lambda d: (lambda v: (d.update(c=3), len(v))[1])(d.keys()))(mk()))
show("view.in", lambda: (lambda d: (lambda v: (d.__setitem__("c", 3), "c" in v)[1])(d.keys()))(mk()))
show("view.stale", lambda: (lambda d: (lambda it: (next(it), d.update(c=3), next(it)))(iter(d.keys())))(mk()))
show("view.val.stale", lambda: (lambda d: (lambda it: (next(it), d.update(c=3), next(it)))(iter(d.values())))(mk()))
show("view.items.stale", lambda: (lambda d: (lambda it: (next(it), d.update(c=3), next(it)))(iter(d.items())))(mk()))
show("view.replace.ok", lambda: (lambda d: (lambda it: (next(it), d.__setitem__("a", 9), next(it)))(iter(d.keys())))(mk()))
show("view.repr", lambda: repr(mk().items()))
show("ior.self", lambda: (lambda d: d.__ior__({"c": 3}) is d)(mk()))
