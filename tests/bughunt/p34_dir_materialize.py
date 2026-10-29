# p34: dir()/getattr materialization for list & tuple structural dunders.
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# dir() content parity for the structural-only builtin types.
show("dir-list", lambda: dir(list))
show("dir-tuple", lambda: dir(tuple))

# The names are now present via attribute lookup, not just dir().
show("hasattr", lambda: [hasattr(list, n) for n in ("__reversed__", "__iter__", "__len__", "__contains__")])
show("tuple-hasattr", lambda: [hasattr(tuple, n) for n in ("__iter__", "__contains__", "__getnewargs__")])

# Explicit dunder calls delegate to the real implementation.
show("list.__reversed__", lambda: list(list.__reversed__([1, 2, 3])))
show("list.__reversed__ name", lambda: type(list.__reversed__([1, 2, 3])).__name__)
show("tuple.__iter__", lambda: list(tuple.__iter__((1, 2, 3))))
show("tuple.__contains__", lambda: tuple.__contains__((1, 2, 3), 2))
show("tuple.__contains__ false", lambda: tuple.__contains__((1, 2, 3), 9))
show("tuple.__getnewargs__", lambda: tuple.__getnewargs__((1, 2)))

# Descriptor types / reprs.
for t, n in [(list, "__reversed__"), (tuple, "__iter__"), (tuple, "__contains__"), (tuple, "__getnewargs__")]:
    d = getattr(t, n)
    show(f"kind {t.__name__}.{n}", lambda d=d: (type(d).__name__, repr(d)))

# Syntax still uses the CLR path (no recursion, subclasses unaffected).
show("syntax", lambda: [list(reversed([1, 2, 3])), 2 in (1, 2, 3), list((1, 2))])
