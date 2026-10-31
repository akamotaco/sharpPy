"""P0-54 measurement: what a bound method compares and hashes like.

  C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe .artifacts/tmp/l22_method_eq.py

CPython: Objects/classobject.c:245 method_richcompare - equal when __func__ == and __self__
is (pointer identity, :261), NotImplemented against everything else.  method_hash (:299) is
hash(__self__) pointer mixed with hash(__func__).
"""
import subprocess
import sys
import os

ROOT = r"C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy"
DOTNET = r"C:/Users/AMD-MAX395-128G-2T/AppData/Local/SharpPy/dotnet/dotnet.exe"
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")
PY = """
class O:
    def f(self):
        return 1
    @staticmethod
    def s():
        return 2
    @classmethod
    def c(cls):
        return 3
    def g(self):
        return 1

class P(O):
    pass

class Weird:
    def f(self):
        return 1
    def __eq__(self, other):
        print('  eq called on', type(self).__name__)
        return True

o = O()
p = O()

def show(label, fn):
    try:
        print("%-28s %r" % (label, fn()))
    except Exception as exc:
        print("%-28s %s: %s" % (label, type(exc).__name__, exc))

show("o.f == o.f", lambda: o.f == o.f)
show("o.f != o.f", lambda: o.f != o.f)
show("o.f == p.f", lambda: o.f == p.f)
show("o.f == O.f", lambda: o.f == O.f)
show("O.f == O.f", lambda: O.f == O.f)
show("o.f == o.g", lambda: o.f == o.g)
show("o.f == o.s", lambda: o.f == o.s)
show("o.c == o.c", lambda: o.c == o.c)
show("o.c == O.c", lambda: o.c == O.c)
show("o.s == o.s", lambda: o.s == o.s)
show("o.f == 5", lambda: o.f == 5)
show("o.f == None", lambda: o.f == None)
show("type(o.f)", lambda: type(o.f).__name__)
show("o.f.__func__ is o.f.__func__", lambda: o.f.__func__ is o.f.__func__)
show("o.f.__self__ is o", lambda: o.f.__self__ is o)
show("hash(o.f) == hash(o.f)", lambda: hash(o.f) == hash(o.f))
show("hash(o.f) != hash(p.f)", lambda: hash(o.f) != hash(p.f))
show("hash(o.f) == hash(o.g)", lambda: hash(o.f) == hash(o.g))
show("{o.f: 1}[o.f]", lambda: {o.f: 1}[o.f])
show("{o.f}.add(p.f)", lambda: (lambda s: (s.add(p.f), len(s)))({o.f}))
show("o.f == Weird().f", lambda: o.f == Weird().f)
w = Weird()
show("w.f == w.f", lambda: w.f == w.f)

import atexit
m = o.f
atexit.register(m)
show("unregister(o.m) removes", lambda: atexit.unregister(o.f) or atexit._ncallbacks())
show("ncallbacks after", lambda: atexit._ncallbacks())
atexit._clear()
"""
path = os.path.join(ROOT, ".artifacts", "tmp", "l22_m.py")
open(path, "w", encoding="utf-8").write(PY)
ref = subprocess.run([sys.executable, path], capture_output=True, text=True, encoding="utf-8")
sp = subprocess.run([DOTNET, DLL, path], capture_output=True, text=True, encoding="utf-8")
for line, a, b in zip(ref.stdout.splitlines(), ref.stdout.splitlines(), sp.stdout.splitlines()):
    print(("  ok  " if a == b else "  DIFF") + " " + a)
    if a != b:
        print("        sp-> " + b)
if ref.stderr.strip():
    print("ref err", ascii(ref.stderr.strip()[:140]))
if sp.stderr.strip():
    print("sp  err", ascii(sp.stderr.strip()[:140]))
