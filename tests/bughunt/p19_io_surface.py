# p19: io module surface parity (TextIOWrapper / concrete types / open identity)
# CPython 3.12.15 reference. Handles are always closed (SharpPy has no refcount GC,
# so an abandoned file keeps an OS lock; that is tested separately, not relied on).
import io
import builtins
import os

sc = (os.environ.get("BUGHUNT_SCRATCH") or ".").replace("\\", "/")
p = sc + "/p19io.txt"


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


print("dir", [n for n in dir(io) if not n.startswith("_")])
print("all", io.__all__)

# __module__ / repr of the base ABCs (io) vs the concrete _io classes
for name in ["IOBase", "RawIOBase", "BufferedIOBase", "TextIOBase"]:
    print("base", name, repr(getattr(io, name)))
for name in ["TextIOWrapper", "BufferedReader", "BufferedWriter", "BufferedRWPair",
             "BufferedRandom", "FileIO", "IncrementalNewlineDecoder",
             "StringIO", "BytesIO"]:
        k = getattr(io, name)
        print("conc", name, repr(k), k.__module__)

print("open is builtins.open", io.open is builtins.open)
print("has open_code", callable(io.open_code), "has text_encoding", callable(io.text_encoding))
print("text_encoding utf-8", io.text_encoding("utf-8"))
print("text_encoding ascii", io.text_encoding("ascii"))
show("text_encoding no-arg", lambda: io.text_encoding())

# UnsupportedOperation is the class a wrong-mode file operation raises
f = open(p, "w")
try:
    f.read()
except Exception as e:
    print("w.read type", type(e).__name__, isinstance(e, io.UnsupportedOperation),
          isinstance(e, OSError), isinstance(e, ValueError))
    print("w.read mro", [b.__name__ for b in type(e).__mro__])
f.close()

# text and binary open produce the concrete types. isinstance against the concrete
# _io type works by type identity. (isinstance against io.IOBase and the ABC bases
# needs the metaclass __instancecheck__ protocol, tracked as a separate cluster.)
tf = open(p, "r")
print("type r", type(tf).__name__,
      isinstance(tf, io.TextIOWrapper), tf.__class__ is io.TextIOWrapper)
tf.close()

bf = open(p, "rb")
print("type rb", type(bf).__name__,
      isinstance(bf, io.BufferedReader), isinstance(bf, io.FileIO))
bf.close()

wf = open(p, "wb")
print("type wb", type(wf).__name__, isinstance(wf, io.BufferedWriter))
wf.close()

os.remove(p)
