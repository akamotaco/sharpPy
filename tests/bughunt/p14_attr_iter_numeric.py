# p14: attribute protocol, iteration protocol, complex, slice edge cases
def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

# --- attribute protocol ---
class GA:
    def __getattr__(self, name):
        if name == "magic":
            return 42
        raise AttributeError(name)

show("getattr.hook", lambda: GA().magic)
show("getattr.miss", lambda: GA().nope)
show("getattr.set.get", lambda: (setattr(GA(), "x", 1), GA().x)[1] if False else (lambda g: (setattr(g, "x", 1), g.x, g.magic)[1:])(GA()))

class OldStyle:
    def __getattr__(self, n): return "ga:" + n
    def __getattribute__(self, n):
        if n == "special":
            return "ga2"
        return object.__getattribute__(self, n)

show("getattr.custom", lambda: OldStyle().special)
show("getattr.fallback", lambda: OldStyle().anything)

class SA:
    def __setattr__(self, name, value):
        object.__setattr__(self, "_seen", name)
        raise ValueError("blocked")
show("setattr.block", lambda: setattr(SA(), "z", 1))

class Props:
    @property
    def x(self): return "prop"
show("prop.class.access", lambda: type(Props().__class__.x).__name__)
show("prop.instance", lambda: Props().x)

class CM:
    @classmethod
    def m(cls): return ("cm", cls.__name__)
class CMSub(CM): pass
show("classmethod.sub", lambda: CMSub.m())
show("classmethod.inst", lambda: CMSub().m())

class SM:
    @staticmethod
    def s(a): return ("sm", a)
show("staticmethod", lambda: SM().s(1))
show("staticmethod.class", lambda: SM.s(2))

class DelP:
    @property
    def y(self): return 1
show("prop.del.err", lambda: delattr(DelP(), "y"))

class SuperExplicit:
    def m(self): return "base"
class ChildSE(SuperExplicit):
    def m(self): return "child"
    def via(self): return super(ChildSE, self).m()
show("super.explicit", lambda: ChildSE().via())

# --- iteration protocol ---
class SeqOld:
    def __getitem__(self, i):
        if i >= 3: raise IndexError
        return i * i
show("iter.oldstyle", lambda: list(SeqOld()))
show("iter.oldstyle.next", lambda: next(iter(SeqOld())))

class Both:
    def __iter__(self): return iter(["new"])
    def __getitem__(self, i): return "old"
show("iter.prefers_iter", lambda: list(Both()))

class StopInGen:
    def __iter__(self):
        yield 1
        return "ret"
sg = StopInGen()
it = iter(sg)
vals = [next(it)]
try:
    next(it)
except StopIteration as se:
    vals.append(se.value)
show("iter.genreturn", vals)

show("enumerate.start", lambda: list(enumerate("ab", 3)))
show("enumerate.tuple1", lambda: list(enumerate(["x"])))
show("reversed.str", lambda: "".join(reversed("abc")))
show("reversed.range", lambda: list(reversed(range(3))))
show("reversed.nonseq", lambda: reversed(5))
show("reversed.custom", lambda: list(reversed(type("R", (), {"__reversed__": lambda s: iter([1, 2])})())))

# --- tuple += identity ---
t1 = (1, 2)
tid = id(t1)
t1 += (3,)
show("tuple.inplace", lambda: id(t1) == tid)

l1 = [1]
lid = id(l1)
l1 += [2]
show("list.inplace", lambda: id(l1) == lid)

# --- extended slice errors ---
def t5(tag, fn):
    show(tag, fn)
show("ext.slice.len", lambda: (lambda x: (x.__setitem__(slice(None, None, 2), [9, 9, 9, 9]), x)[1])([1, 2, 3, 4, 5]))
show("ext.del.len", lambda: (lambda x: (x.__delitem__(slice(None, None, 2)), x)[1])([1, 2, 3, 4]))
show("slice.assign.itert", lambda: (lambda x: (x.__setitem__(slice(0, 2), iter("ab")), x)[1])([0, 0, 0]))
show("slice.assign.nonseq", lambda: (lambda x: (x.__setitem__(slice(0, 2), 5), x)[1])([0, 0, 0]))
show("tuple.ext.assign", lambda: (1, 2, 3).__setitem__(slice(None, None, 2), [7]))

# --- complex ---
c = 3 + 4j
show("complex.basic", lambda: (c.real, c.imag))
show("complex.abs", lambda: abs(c))
show("complex.add", lambda: c + (1 - 4j))
show("complex.mul", lambda: (1j) * (1j))
show("complex.div", lambda: (4 + 2j) / 2)
show("complex.pow", lambda: (1j) ** 2)
show("complex.repr", lambda: repr(1 + 0j) + repr(complex(0, -1)))
show("complex.eq", lambda: (3 + 0j) == 3)
show("complex.order", lambda: c < 1)
show("complex.conj", lambda: c.conjugate())
show("complex.float", lambda: float(1 + 0j))
show("complex.int", lambda: int(1 + 2j))
show("complex.format", lambda: format(complex(1, 2), ".2f"))

# --- numeric misc ---
show("int.base0", lambda: int("0x1f", 0))
show("int.base36", lambda: int("z", 36))
show("int.float_trunc", lambda: (int(3.99), int(-3.99)))
show("int.underscore", lambda: int("1_000"))
show("int.bad.underscore", lambda: int("_1"))
show("floordiv.neg", lambda: (-7) // 2)
show("mod.neg", lambda: (-7) % 2)
show("floordiv.zero", lambda: 7 // 0)
show("pow.three", lambda: pow(2, 10, 1000))
show("float.fromstr", lambda: float("1e400"))
show("complex.str", lambda: complex("1+2j"))
