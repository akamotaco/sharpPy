# p18: importlib.reload real semantics
# CPython 3.12 ground truth: reload re-executes source in the EXISTING module
# __dict__ (no clearing), preserves module identity + sys.modules entry, returns
# the module, and propagates the original exception on failure (partial update stays).
import importlib
import os
import sys

base = (os.environ.get("BUGHUNT_SCRATCH") or ".").replace("\\", "/")
mpath = base + "/p18_mod.py"


def write(s):
    with open(mpath, "w", encoding="utf-8") as f:
        f.write(s)


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


for p in (base, "", "."):
    if p and p not in sys.path:
        sys.path.insert(0, p)

# v1: baseline definitions
write("VALUE = 1\nCOUNT = 0\nSTALE = 'keep'\nprint('exec-v1')\n")
import p18_mod as m
print("after-import", m.VALUE, m.COUNT, m.STALE)
show("identity", lambda: importlib.reload(m) is m)
print("after-reload", m.VALUE, m.COUNT, m.STALE)
print("reload-in-sysmodules", sys.modules["p18_mod"] is m)

# v2: VALUE changed, COUNT increments, NEW added, STALE dropped (must remain stale)
write("VALUE = 2\nCOUNT = COUNT + 1\nNEW = 3\nprint('exec-v2')\n")
show("reload2-return-self", lambda: importlib.reload(m) is m)
print("v2", m.VALUE, m.COUNT, m.NEW, m.STALE)

# argument / type errors
show("reload-count-args", lambda: importlib.reload())
show("reload-two-args", lambda: importlib.reload(m, True))
show("reload-nonmodule", lambda: importlib.reload(42))
show("reload-str", lambda: importlib.reload("p18_mod"))

# builtin module without a source file returns unchanged
show("reload-sys", lambda: importlib.reload(sys))

print("dunders", m.__name__ == "p18_mod", bool(m.__file__))
show("returns-sysmodules-entry", lambda: importlib.reload(m) is sys.modules["p18_mod"])

# failing reload: original exception propagates, partial update, still registered
write("VALUE = 9\nBOOM = 1 / 0\nAFTER = 'no'\n")
show("reload-fail", lambda: importlib.reload(m))
print("after-fail", m.VALUE, hasattr(m, "AFTER"), sys.modules.get("p18_mod") is m)

try:
    os.remove(mpath)
except OSError:
    pass
