"""CPython and SharpPy read each other's marshal data, for every file of a corpus.

Under CPython this writes code objects, has SharpPy read and rewrite them, and compares what
comes back. Then it runs each file from the marshalled code under SharpPy and compares the
output with CPython's own run.

usage: python tools/check_marshal_cross.py <directory> ...
"""
import marshal
import os
import subprocess
import sys
import tempfile
import types

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOTNET = os.path.expanduser("~/AppData/Local/SharpPy/dotnet/dotnet.exe")
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")
FIELDS = ("co_argcount", "co_posonlyargcount", "co_kwonlyargcount", "co_nlocals", "co_stacksize",
          "co_flags", "co_firstlineno", "co_code", "co_names", "co_varnames", "co_freevars",
          "co_cellvars", "co_filename", "co_name", "co_qualname", "co_linetable",
          "co_exceptiontable")

REWRITE = """
import marshal, sys
with open(sys.argv[1], 'rb') as f:
    code = marshal.load(f)
with open(sys.argv[2], 'wb') as f:
    marshal.dump(code, f)
"""

RUN = """
import marshal, sys
path = sys.argv[1]
with open(path, 'rb') as f:
    code = marshal.load(f)
sys.argv = sys.argv[2:]
exec(code, {'__name__': '__main__', '__file__': sys.argv[0], '__builtins__': __builtins__})
"""


def differs(a, b):
    for field in FIELDS:
        if getattr(a, field) != getattr(b, field):
            return f"{a.co_name}.{field}"
    if len(a.co_consts) != len(b.co_consts):
        return f"{a.co_name}.co_consts size"
    for x, y in zip(a.co_consts, b.co_consts):
        if isinstance(x, types.CodeType):
            found = differs(x, y)
            if found:
                return found
        elif type(x) is not type(y) or (x != y and x == x):
            return f"{a.co_name}.co_consts {x!r} {y!r}"
    return None


def sharppy(script, *args, timeout=60):
    return subprocess.run([DOTNET, DLL, script, *args], capture_output=True, timeout=timeout)


def plain(raw):
    return raw.replace(b"\r\n", b"\n")


def main():
    bad = good = ran = 0
    with tempfile.TemporaryDirectory() as folder:
        rewrite = os.path.join(folder, "rewrite.py")
        run = os.path.join(folder, "run.py")
        with open(rewrite, "w") as f:
            f.write(REWRITE)
        with open(run, "w") as f:
            f.write(RUN)
        for top in sys.argv[1:]:
            for name in sorted(os.listdir(top)):
                if not name.endswith(".py"):
                    continue
                path = os.path.join(top, name)
                with open(path, encoding="utf-8") as f:
                    code = compile(f.read(), path, "exec")
                source, target = os.path.join(folder, "a"), os.path.join(folder, "b")
                with open(source, "wb") as f:
                    marshal.dump(code, f)
                done = sharppy(rewrite, source, target)
                if done.returncode != 0:
                    bad += 1
                    print("REWRITE", name, done.stderr[-300:].decode("utf-8", "replace"))
                    continue
                with open(target, "rb") as f:
                    found = differs(code, marshal.load(f))
                if found:
                    bad += 1
                    print("DIFF", name, found)
                    continue
                good += 1
                expected = subprocess.run([sys.executable, path], capture_output=True, timeout=120)
                actual = sharppy(run, source, path)
                ran += 1
                if plain(expected.stdout) != plain(actual.stdout):
                    bad += 1
                    print("OUTPUT", name, actual.stderr[-300:].decode("utf-8", "replace"))
    print("rewritten", good, "ran", ran, "bad", bad)


main()
