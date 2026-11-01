exec("x1 = 1")
print(x1, eval("x1 + 1"), "x1" in globals(), dir()[:0])
exec("import sys\nsamples = {'k': 1}\nfor k in samples:\n    print(k)\n")
print(samples, sys.__name__)


def f():
    exec("y = 2")
    print(locals(), dir())
    try:
        print(y)
    except NameError as e:
        print(e)
    exec("global gv\ngv = 3")
    d = {}
    exec("w = 4", None, d)
    print(d, eval("gv + 1"), eval("q + 1", {"q": 1}), eval("a", {}, {"a": 5}))


f()
print(gv)
g = {}
exec("z = 1\ndef h():\n    return z", g)
g["z"] = 2
print(sorted(k for k in g if k != "__builtins__"), g["h"]())


class C:
    exec("attr = 1")
    print(sorted(k for k in locals() if not k.startswith("__")))


print(C.attr)
ns = {"__name__": "mod"}
exec("class K: pass\nprint(K.__module__)\nprint(__name__)", ns)
