"""Check that SharpPy's .pyc magic number moves with its compiler (core/PycMagic.cs).

Run under SharpPy. Every top-level module of CPython 3.12.15's Lib (../cpython-v3.12.15, a fixed
corpus that adding a module to SharpPy's Lib does not change) is compiled and marshalled; the digest of that
output, with importlib.util.MAGIC_NUMBER, is compared with tools/bytecode_fingerprint.txt. A new
digest under the recorded magic number means the compiler's output changed without a new magic
number, and caches written before would still be read: bump PycMagic.Number, then record the new
pair with --update.

usage: sharppy tools/bytecode_fingerprint.py [--update]
"""
import hashlib
import importlib.util
import marshal
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RECORD = os.path.join(ROOT, "tools", "bytecode_fingerprint.txt")


def digest():
    lib = os.path.join(ROOT, "..", "cpython-v3.12.15", "Lib")
    h = hashlib.sha256()
    for name in sorted(os.listdir(lib)):
        if not name.endswith(".py"):
            continue
        with open(os.path.join(lib, name), "rb") as stream:
            source = stream.read()
        # The file name is part of the code object; a fixed one keeps the digest the same anywhere.
        code = compile(source, "Lib/" + name, "exec", dont_inherit=True)
        h.update(name.encode("ascii"))
        h.update(marshal.dumps(code))
    return h.hexdigest()


def main():
    magic = int.from_bytes(importlib.util.MAGIC_NUMBER[:2], "little")
    current = f"{magic} {digest()}"
    if "--update" in sys.argv:
        with open(RECORD, "w", encoding="ascii", newline="\n") as stream:
            stream.write(current + "\n")
        print("MAGIC recorded", current)
        return
    with open(RECORD, encoding="ascii") as stream:
        recorded = stream.read().strip()
    recorded_magic = int(recorded.split()[0])
    if current == recorded:
        print("MAGIC ok", magic)
    elif magic != recorded_magic:
        print("MAGIC changed", recorded_magic, "->", magic, "- record it with --update")
    else:
        print("MAGIC STALE: the compiler's output changed under magic", magic,
              "- bump PycMagic.Number (core/PycMagic.cs), then run with --update")


main()
