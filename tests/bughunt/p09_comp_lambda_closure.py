def show(label, fn):
    try:
        print(f"{label!r}: {fn()!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e}")

def mk():
    return [lambda: i for i in range(3)]
show("closure.late.bind", lambda: [g() for g in mk()])

def mk_cond():
    return [lambda: (i, j) for i in range(2) for j in range(2)]
show("closure.double.loop", lambda: [g() for g in mk_cond()])

def mk_free():
    a = "free"
    return [(lambda: a)() for _ in range(2)]
show("comprehension.lambda.freevar", mk_free)

f_module = [lambda: k for k in range(2)]
show("module.comp.lambda", lambda: [g() for g in f_module])
