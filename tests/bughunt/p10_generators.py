# p10: generator / coroutine protocol parity probes (CPython 3.12 reference)
def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

def gen():
    yield 1
    yield 2

g = gen()
show("gen.first", lambda: next(g))
show("gen.next", lambda: next(g))
show("gen.stop", lambda: next(g))
show("gen.stop2", lambda: next(g))
show("gen.after.type", lambda: type(next(iter([])), None) and "x")

g2 = gen()
show("gen.send.first", lambda: g2.send("x"))
show("gen.send.mid", lambda: g2.send("y"))

g3 = gen()
show("gen.close", lambda: g3.close() or "closed")
show("gen.after.close", lambda: next(g3))

def valgen():
    x = yield 1
    yield x

g4 = valgen()
next(g4)
show("gen.send.value", lambda: g4.send(42))

g5 = valgen()
next(g5)
show("gen.throw", lambda: g5.throw(ValueError("boom")))

def catchgen():
    try:
        yield "in"
    except ValueError:
        yield "caught"

g6 = catchgen()
next(g6)
show("gen.throw.caught", lambda: g6.throw(ValueError("v")))

def fin():
    try:
        yield "body"
    finally:
        print("fin-finally")

g7 = fin()
show("gen.close.finally", lambda: next(g7))
g7.close()

def retgen():
    try:
        yield 1
    except GeneratorExit:
        print("got-exit")

g8 = retgen()
next(g8)
show("gen.exit", lambda: g8.close())

def delegate():
    yield from [10, 20]
    return "done"

gd = delegate()
show("yfrom.1", lambda: next(gd))
show("yfrom.2", lambda: next(gd))
show("yfrom.return.value", lambda: gd.gi_frame is not None)

def outer():
    v = yield from valgen()
    return ("captured", v)

go = outer()
next(go)
go.send(1)
show("yfrom.send", lambda: (go.send(2), ))

def yf_stop():
    try:
        yield from iter([])
    except GeneratorExit:
        print("never")
    return 5

gs = yf_stop()
show("yfrom.empty", lambda: gs.send(None))
show("yfrom.empty.stop", lambda: next(gs))

show("gen.reenter", lambda: g2.send.__self__ is g2)

def yf_ret():
    r = yield from gen()
    return r

gr = yf_ret()
show("yfrom.collect", lambda: list(gr))

async def agen():
    return 7

_c = agen()
show("coro.type", lambda: type(_c).__name__)
show("coro.no.iter", lambda: iter(_c))
_c.close()

def infinite():
    i = 0
    while True:
        yield i
        i += 1

gi = infinite()
show("inf.take3", lambda: [next(gi), next(gi), next(gi)])
gi.close()
show("inf.closed", lambda: gi.gi_running)

show("gen.iterator.identity", lambda: iter(gen()) is iter(gen) or "skip")

def genlocal():
    total = 0
    for i in range(3):
        resp = yield i
        if resp is not None:
            total += resp
    return total

gl = genlocal()
vals = [next(gl), gl.send(10), gl.send(20)]
try:
    gl.send(30)
except StopIteration as se:
    vals.append(se.value)
print("gen.accumulate", vals)
