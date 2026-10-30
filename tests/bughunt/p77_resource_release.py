# p77: external resource release. A file object Python can no longer reach must not keep the file
# locked. CPython guarantees this with reference counting (Modules/_io/iobase.c:298 iobase_finalize
# calls close() when the last reference drops); SharpPy's lifetime is the CLR collector's, so the
# release bus (core/PyResourceBus.cs) owes the same outcome. Only cases that are deterministic in
# BOTH engines are pinned here.
import os
import gc

# Scratch directory convention from p16: the harness injects one shared directory, and there is no
# `tempfile` module in SharpPy yet.
tmp_base = (os.environ.get("BUGHUNT_SCRATCH")
            or os.environ.get("TMPDIR")
            or os.environ.get("TMP")
            or os.environ.get("TEMP")
            or ".").replace("\\", "/")
d = tmp_base + "/p77_bus"


def attempt(label, fn):
    try:
        fn()
        print(label, "ok")
    except Exception as e:
        print(label, type(e).__name__)


try:
    os.rmdir(d)
except OSError:
    pass
os.mkdir(d)

# 1) A rebinding leak loop: every file object becomes unreachable at the next assignment.
#    This is the case that measured PermissionError on os.remove before the release bus.
p = d + "/rebind.txt"


def leak(n, path):
    for i in range(n):
        f = open(path, "a")
        f.write("x")


leak(300, p)
attempt("rebind remove", lambda: os.remove(p))

# 2) Dropped container, then the explicit reconciliation point.
p2 = d + "/listdrop.txt"
handles = [open(p2, "a") for _ in range(200)]
attempt("held remove", lambda: os.remove(p2))
handles = None
gc.collect()
attempt("drop+collect remove", lambda: os.remove(p2))

# 3) An abandoned write must still be readable, and the file removable.
p3 = d + "/write.txt"
open(p3, "w").write("hello")
print("abandoned content", repr(open(p3).read()))
attempt("abandoned remove", lambda: os.remove(p3))

# 4) `with` still closes, and .closed reports it.
p4 = d + "/with.txt"
f = open(p4, "w")
with f as g:
    g.write("data")
print("closed after with", f.closed)
attempt("with remove", lambda: os.remove(p4))

# 5) A file Python still references must never be released early, even across a collection.
p5 = d + "/live.txt"
f = open(p5, "w")
f.write("one")
gc.collect()
f.write("two")
f.close()
print("live content", repr(open(p5, "r").read()))
try:
    f.read()
except Exception as e:
    print("read after close", type(e).__name__, e)

# 6) rename after the writer was dropped.
p6 = d + "/old.txt"
open(p6, "w").write("r")
attempt("rename after drop", lambda: os.rename(p6, d + "/new.txt"))
attempt("rename back", lambda: os.rename(d + "/new.txt", p6))

# 7) rmdir on a non-empty directory keeps reporting the directory, not a stale handle.
open(d + "/inner.txt", "w").write("i")
attempt("rmdir nonempty", lambda: os.rmdir(d))
attempt("inner remove", lambda: os.remove(d + "/inner.txt"))

# 8) Many abandoned handles: the count must not decide the outcome, and a still-held handle must
#    still be visible as a locked file.
p8 = d + "/many.txt"
last = None
for i in range(1500):
    fh = open(p8, "a")
    fh.write("y")
    last = fh
attempt("held-last remove", lambda: os.remove(p8))
last = None
fh = None
attempt("released-all remove", lambda: os.remove(p8))

# 9) Line iteration through with-blocks is unaffected.
p9 = d + "/lines.txt"
with open(p9, "w") as fh:
    fh.write("a\nb\nc\n")
with open(p9) as fh:
    print("lines", [line.rstrip("\n") for line in fh])
attempt("lines remove", lambda: os.remove(p9))

for name in sorted(os.listdir(d)):
    attempt("cleanup " + name, lambda name=name: os.remove(d + "/" + name))
attempt("final rmdir", lambda: os.rmdir(d))
