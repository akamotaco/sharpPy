# p29: map()/filter() reject keyword args; list.index(value, start, stop).
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# map(): positional only (no strict= in CPython 3.12), unlike zip().
show("map-kwargs", lambda: list(map(str, [1], key=2)))
show("map-strict", lambda: list(map(str, [1], strict=True)))
show("map-ok", lambda: list(map(str, [1, 2])))
show("map-noargs", lambda: map())
show("map-onearg", lambda: map(str))
show("filter-kwargs", lambda: list(filter(None, [1, 0], key=2)))
show("filter-ok", lambda: list(filter(None, [0, 1, 2])))
show("zip-strict-ok", lambda: list(zip([1, 2], [3, 4], strict=True)))


def zerr():
    list(zip([1, 2, 3], [1], strict=True))
show("zip-strict-err", zerr)

# list.index(value, start=0, stop=len)
show("idx-ss", lambda: [1, 2, 3, 2].index(2, 2))
show("idx-s", lambda: [1, 2, 3, 1].index(1, 1))
show("idx-stop", lambda: [1, 2, 3, 1].index(1, 0, 2))
show("idx-bigstop", lambda: [1, 2, 3, 1].index(1, 0, 99))
show("idx-negstart", lambda: [1, 2, 3, 1].index(1, -2))
show("idx-startgtstop", lambda: [1, 2, 3, 1].index(1, 3, 1))
show("idx-noargs", lambda: [1, 2].index())
show("idx-miss", lambda: [1, 2].index(9))
show("idx-badstart", lambda: [1, 2].index(1, "a"))
show("idx-kwargs", lambda: [1, 2].index(1, start=0))
show("idx-3args", lambda: [1, 2].index(1, 0, 1, 2))
