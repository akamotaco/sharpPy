log = []
def f(label):
    log.append(label)
    return label

def take(a, b, c=None, *rest, d=4, **kw):
    return (a, b, c, rest, d, kw)

def show(label, fn):
    global log
    log = []
    try:
        r = fn()
        print(f"{label!r}: {r!r} order={log!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e} order={log!r}")

show("pos.eval.order", lambda: take(f("a"), f("b"), f("c")))
show("kw.eval.order", lambda: take(f("a"), f("b"), d=f("d"), z=f("z")))
show("unpack.after.kw", lambda: take(f("a"), *[f("b"), f("c")], **{"d": f("d")}))
show("double.star", lambda: take(**{"a": f("a"), "b": f("b"), "c": f("c")}))
show("missing.arg", lambda: take())
show("too.many", lambda: take(1, 2, 3, 4, 5, 6))
show("dup.kw", lambda: take(1, 2, d=3, **{"d": 4}))
show("dup.pos.kw", lambda: take(1, 2, a=3))
show("kw.before.pos.call", lambda: take(*[1], 2))
show("star.unused.iter.consume", lambda: take(*iter([1, 2, 3, 4])))
show("kw.key.nonstr", lambda: take(**{1: 2}))
show("lambda.default.bind", lambda: (lambda f=[]: f)(1) and "bad" or "reused")
def mk2():
    return [(lambda i=i: i)() for i in range(3)]
show("default.captures.now", lambda: mk2())
show("nonlocal.missing", lambda: compile("def f():\n nonlocal x\n return x", "<s>", "exec") and "compiled")
show("global.assign.local.shadow", lambda: (lambda: globals().update(G1=5) or (lambda G1: G1)(1) )())
x_g = "module"
def uses_global():
    global x_g
    x_g = "changed"
    return x_g
show("global.write", lambda: (uses_global(), x_g))
def shadow_del():
    y = 1
    del y
    try:
        return y
    except NameError as e:
        return f"NameError {e}"
show("del.local.then.use", shadow_del)
show("call.noncallable", lambda: (lambda: 1)())
show("call.int", lambda: (1)())
show("method.unbound.missing.self", lambda: [1].index == [1].index and len([1].index.__self__) if False else [1].index.__self__)
show("star.expr.target", lambda: (lambda: (lambda *a, b: (a, b))(1, 2, b=3))())
show("keyword.only.star", lambda: (lambda *, a: a)(1))
show("annot.eval", lambda: (lambda x: x).__annotations__ if False else "skip")
exec_ns = {}
exec("def deco(f):\n def w(): return f() + '!'\n return w\n@deco\ndef g(): return 'hi'", exec_ns)
show("decorator.exec", lambda: exec_ns["g"]())
def deco_order():
    seq = []
    def d1(f): seq.append("d1"); return f
    def d2(f): seq.append("d2"); return f
    def body(): seq.append("body")
    @d1
    @d2
    def g(): body()
    g()
    return seq
show("decorator.order", deco_order)
show("default.eval.def.time", lambda: (lambda: (lambda v=print and 7: v)())())
class Counter:
    calls = 0
    def __call__(self, *a, **k):
        Counter.calls += 1
        return (a, sorted(k))
show("callable.instance", lambda: (Counter()(1, x=2), Counter.calls))
