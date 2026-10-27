def show(label, fn):
    try:
        print(f"{label!r}: {fn()!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e}")

def outer():
    x = 1
    def inner():
        nonlocal x
        x += 1
        return x
    return [inner(), inner(), (lambda: x)()]
show("nonlocal.cell", outer)

def counter():
    n = 0
    def inc():
        nonlocal n
        n += 1
    inc(); inc()
    return n
show("cell.incr", counter)

def del_cell():
    x = 5
    def get():
        return x
    del x
    try:
        get()
    except NameError as e:
        return f"NameError: {e}"
show("del.then.cell.read", del_cell)

def loop_var():
    fs = []
    for i in range(3):
        def g():
            return i
        fs.append(g)
    return [f() for f in fs]
show("closure.loop.var", loop_var)

comp = [x for x in range(3)]
show("comp.leaks.py3", lambda: "x" in globals())

def class_scope():
    a = 1
    class C:
        b = a + 1
        try:
            d = [a, b]
        except NameError as e:
            d = f"NameError: {e}"
    return (C.b, C.d)
show("class.body.scope", class_scope)

def nested_free():
    a = 1
    def mid():
        b = 2
        def inner():
            return a + b
        return inner()
    return mid()
show("nested.freevars", nested_free)

def comp_uses_nonlocal():
    total = 0
    def add():
        nonlocal total
        total += sum(x * x for x in range(4))
    add()
    return total
show("comp.in.nonlocal.fn", comp_uses_nonlocal)

g_list = [v for v in range(3)]
show("global.comp", lambda: g_list)

def comprehension_over_mutable():
    d = {i: i for i in range(3)}
    out = []
    for k in d:
        if k == 1:
            d.pop(2)
        out.append(k)
    return out
show("for.over.dict.pop", comprehension_over_mutable)

def star_unpack_scope():
    it = iter([1, 2, 3])
    a, *b = it
    return a, b
show("unpack.star.rest", star_unpack_scope)

def walrus_scope():
    y = [q := 3, q + 1]
    return y, q
show("walrus.leaks.fn", walrus_scope)

comp_walrus = [w := 10, w]
show("walrus.module", lambda: (comp_walrus, w))

try:
    undefined_name  # noqa
except NameError as e:
    print(f"namerror.msg: {e}")

def frames():
    import sys
    depth = 0
    def recurse(n):
        nonlocal depth
        if n:
            return recurse(n - 1)
        return 0
    return recurse(50)
show("deep.recurse.50", frames)

def recursion_limit_probe():
    try:
        def r(n):
            return r(n + 1)
        r(0)
    except RecursionError:
        return "RecursionError"
show("recursion.limit.hit", lambda: "SKIP" if True else recursion_limit_probe())
