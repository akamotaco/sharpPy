# Pinned against CPython 3.12.15: the reduce/size surface Objects/*.c already has.
# Values that depend on the allocator are compared as kind and monotonicity, not exact bytes
# (docs/known_gaps.md P0-45).

def show(label, fn):
    try:
        print(label, ascii(fn()))
    except Exception as exc:
        print(label, type(exc).__name__, exc)

# str.__getnewargs__ (Objects/unicodeobject.c unicode_getnewargs)
show('str.getnewargs', lambda: 'abc'.__getnewargs__())
show('str.getnewargs.empty', lambda: (''.__getnewargs__(),))
class T(str):
    pass
show('str_sub.getnewargs.value', lambda: T('xyz').__getnewargs__()[0])
show('str_sub.type_after_new', lambda: type(T.__new__(T, 'q')).__name__)

# set.__reduce__ / frozenset.__reduce__ (Objects/setobject.c set_reduce, frozenset_reduce)
show('set.reduce.empty', lambda: set().__reduce__())
show('set.reduce.items', lambda: (set([1, 2]).__reduce__()[0],
                                  sorted(set([1, 2]).__reduce__()[1][0]),
                                  set([1, 2]).__reduce__()[2]))
class S(set):
    pass
r = S([3]).__reduce__()
show('set_sub.reduce', lambda: (r[0].__name__, sorted(r[1][0]), r[2]))
s = S([4])
s.tag = 'kept'
r2 = s.__reduce__()
show('set_sub.state', lambda: (r2[0].__name__, sorted(r2[1][0]),
                               dict(r2[2]) if r2[2] is not None else None))
show('fset.reduce.empty', lambda: frozenset().__reduce__())
show('fset.reduce.items', lambda: (frozenset([1]).__reduce__()[0],
                                   sorted(frozenset([1]).__reduce__()[1][0]),
                                   frozenset([1]).__reduce__()[2]))

# __sizeof__: kind and monotonicity only
show('sizeof.kind', lambda: (type(set().__sizeof__()).__name__,
                             type(frozenset().__sizeof__()).__name__,
                             type({}.__sizeof__()).__name__))
show('sizeof.empty', lambda: (set().__sizeof__() > 0, frozenset().__sizeof__() > 0,
                              {}.__sizeof__() > 0))
show('sizeof.grows', lambda: ({}.__sizeof__() <= {1: 2, 3: 4}.__sizeof__(),
                              set().__sizeof__() <= set(range(50)).__sizeof__(),
                              frozenset().__sizeof__() <= frozenset(range(50)).__sizeof__()))

# The refusals belong to the method_descriptor, not to a Python wrapper. CPython names the
# receiving class in the METH_NOARGS refusal and the defining class in the receiver refusal.
class D(dict):
    pass
show('reduce.arity', lambda: set().__reduce__(1))
show('sizeof.arity', lambda: set().__sizeof__(1))
show('sizeof.arity.subclass', lambda: S().__sizeof__(1))
show('reduce.arity.subclass', lambda: S().__reduce__(1))
show('freduce.arity', lambda: frozenset().__reduce__(1))
show('dict.sizeof.arity', lambda: {}.__sizeof__(1))
show('dict.sizeof.arity.subclass', lambda: D().__sizeof__(1))
show('sizeof.receiver', lambda: set.__sizeof__(3))
show('reduce.receiver', lambda: set.__reduce__(3))
