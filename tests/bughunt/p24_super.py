# p24: super() type/repr/members AND inheritance outcomes vs CPython 3.12.
# Addresses normalized (0xHEX -> 0xADDR). Functional MRO resolution is the core;
# display/type/members were the SharpPy gaps, plus a real LOAD_SUPER_ATTR bug
# where super().static_method() wrongly received self.
import re
ADDR = re.compile(r"0x[0-9a-fA-F]+")
def R(label, fn):
    try:
        print(label, "::", ADDR.sub("0xADDR", repr(fn())))
    except Exception as e:
        print(label, ":: EXC", type(e).__name__, e)

# ---------- display / type / members ----------
class A:
    def f(self): return "A.f"
class B(A):
    def f(self): return "B.f->" + super().f()
    def cm(cls): return type(super(B, cls)).__name__

R("type-super2", lambda: type(super(B, B())))
R("repr-super2", lambda: repr(super(B, B())))
R("repr-super-cls", lambda: repr(super(B, B)))
R("thisclass", lambda: super(B, B()).__thisclass__ is B)
R("self", lambda: isinstance(super(B, B()).__self__, B))
R("selfclass", lambda: super(B, B()).__self_class__ is B)
R("super.class", lambda: super(B, B()).__class__ is type(super(B, B())))
R("unbound-super-type", lambda: type(super(B)))
R("unbound-super-repr", lambda: repr(super(B)))
R("unbound-thisclass", lambda: super(B).__thisclass__ is B)
R("unbound-self", lambda: super(B).__self__)
R("unbound-selfclass", lambda: super(B).__self_class__)
R("zeroarg-type", lambda: B().cm())
R("isinstance-super", lambda: isinstance(super(B, B()), type))

# ---------- inheritance outcomes (MRO resolution) ----------
R("B.f", lambda: B().f())
R("super-in-B.f", lambda: super(B, B()).f())
R("super-in-A.f.error", lambda: super(A, B()).f())

class C0:
    def g(self): return "C0.g"
class D0(C0):
    def g(self): return "D0.g+" + super().g()
class E0(D0):
    def g(self): return "E0.g+" + super().g()
R("deep.E0.g", lambda: E0().g())

# cooperative diamond
class Base:
    calls = []
    def m(self): Base.calls.append("Base")
    def __init__(self): Base.calls.append("BaseInit")
class L(Base):
    def m(self): Base.calls.append("L"); super().m()
    def __init__(self): Base.calls.append("LInit"); super().__init__()
class Rr(Base):
    def m(self): Base.calls.append("R"); super().m()
    def __init__(self): Base.calls.append("RInit"); super().__init__()
class Top(L, Rr):
    def m(self): Base.calls.append("Top"); super().m()
    def __init__(self): Base.calls.append("TopInit"); super().__init__()
R("diamond.mro", lambda: [c.__name__ for c in Top.__mro__])
def run_m():
    Base.calls = []; Top().m(); return Base.calls
R("diamond.m-order", run_m)
def run_init():
    Base.calls = []; Top(); return Base.calls
R("diamond.init-order", run_init)

# ---------- classmethod / staticmethod / property / data via super ----------
class CBase:
    @classmethod
    def who(cls): return "CBase"
    @staticmethod
    def swho(): return "SBase"
    val = 10
    @property
    def prop(self): return "CBase.prop"
class CSub(CBase):
    @classmethod
    def who(cls): return "CSub+" + super().who()
    def via_super_static(self): return super().swho()
    def via_super_prop(self): return super().prop
    def via_super_val(self): return super().val
R("cm-super", lambda: CSub.who())
R("cm-super-inst", lambda: CSub().who())
R("sm-via-super", lambda: CSub().via_super_static())
R("sm-via-getattr", lambda: getattr(super(CSub, CSub()), 'swho')() if False else CSub().via_super_static())
R("prop-via-super", lambda: CSub().via_super_prop())
R("val-via-super", lambda: CSub().via_super_val())

# ---------- zero-arg super passthrough to object ----------
class OnlyObj:
    def __init__(self):
        super().__init__()
        self.made = True
R("super-object-init", lambda: OnlyObj().made)

# ---------- argument / lookup errors ----------
R("err-arg1-not-type", lambda: super(1, 2))
R("err-too-many", lambda: super(A, A(), A()))
R("err-bad-obj", lambda: super(B, 42))
R("err-kwargs", lambda: super(type=A))
R("err-attr-missing", lambda: super(B, B()).nope)
