# p12: context managers & operator reflection probes (CPython 3.12)
def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

log = []

class CM:
    def __enter__(self):
        log.append("enter")
        return "val"
    def __exit__(self, t, v, tb):
        log.append(("exit", t.__name__ if t else None))
        return False

with CM() as v:
    log.append(("body", v))
show("cm.basic", lambda: log)

log.clear()
try:
    with CM():
        raise KeyError("k")
except KeyError:
    log.append("reraised")
show("cm.reraise", lambda: log)

class Swallow:
    def __enter__(self): return self
    def __exit__(self, t, v, tb): return t is ValueError

with Swallow():
    print("swallowed-body")

def gen_cm_factory():
    print("gc-enter")
    try:
        yield "gc"
    finally:
        print("gc-exit")

from contextlib import contextmanager
try:
    @contextmanager
    def cmk():
        yield "cmk"
    with cmk() as w:
        print("cmk-body", w)
    show("cm.contextmanager", lambda: "ok")
except BaseException as e:
    show("cm.contextmanager", lambda: (_ for _ in ()).throw(e))

# operator reflection
class R:
    def __radd__(self, other): return ("radd", other)
    def __rmul__(self, other): return ("rmul", other)
    def __rsub__(self, other): return ("rsub", other)

show("refl.radd", lambda: 5 + R())
show("refl.rmul", lambda: "a" * R())

class L:
    def __add__(self, other): return ("ladd", type(other).__name__)
show("refl.ladd", lambda: L() + 5)
show("refl.mixed", lambda: 5 + L() if False else "skip")

class Both:
    def __add__(self, other): return "left"
    def __radd__(self, other): return "right"

show("refl.left.wins", lambda: Both() + Both())

class SubBoth(L):
    def __add__(self, other): return "sub-left"

show("refl.subclass.right", lambda: L() + SubBoth())

class Num:
    def __index__(self): return 3

show("index.slice", lambda: [1,2,3,4,5][Num():Num()+2 if False else Num()])
show("index.hex", lambda: hex(Num()))
show("index.bin", lambda: bin(Num()))
show("index.oct", lambda: oct(Num()))

class BadIndex:
    def __index__(self): return "x"
show("index.bad", lambda: [1,2,3][BadIndex()])

show("divmod", lambda: divmod(17, 5))
show("divmod.neg", lambda: divmod(-17, 5))
show("divmod.float", lambda: divmod(7.5, 2))

class DM:
    def __divmod__(self, other): return ("dm", other)
show("refl.divmod", lambda: DM().__divmod__(1))
show("refl.rdivmod", lambda: 1 .__class__(1) if False else divmod(1, DM()))

class M:
    def __rlshift__(self, other): return ("rl", other)
show("refl.rlshift", lambda: [1] << M())

class Cmp:
    def __eq__(self, other): return True
    def __hash__(self): return 7

show("cmp.in", lambda: Cmp() in [1, 2])
show("cmp.dict", lambda: {Cmp(): 1}.get(5, "nope") if True else None)

class Ord:
    def __lt__(self, other): return True
show("cmp.lt.only", lambda: Ord() < 1)
show("cmp.gt.refl", lambda: 1 > Ord())

class BadCmp:
    def __lt__(self, other): raise IndexError("boom")
show("cmp.propagate", lambda: BadCmp() < 1)

show("unary.neg.refl", lambda: -R() if False else "skip")
class Neg:
    def __neg__(self): return "n"
    def __pos__(self): return "p"
    def __abs__(self): return "a"
    def __invert__(self): return "i"
show("unary.set", lambda: (-Neg(), +Neg(), abs(Neg()), ~Neg()))

show("pow.refl", lambda: 2 .__pow__(3))
class P:
    def __rpow__(self, other): return ("rpow", other)
show("refl.rpow", lambda: 2 ** P())
show("matmul.missing", lambda: 1 @ 2)

class MM:
    def __matmul__(self, other): return ("mm", other)
show("matmul.self", lambda: MM() @ 3)

show("contains.refl", lambda: 1 in (lambda: 1)() if False else "skip")
class Has:
    def __contains__(self, x): return x == 42
show("contains.custom", lambda: 42 in Has())
show("contains.iter.fallback", lambda: "bc" in ["abc", "bc"])
show("contains.str", lambda: "a" in "cat")
show("contains.notimpl", lambda: 1 in 5)
