"""Rebuild every code object of the corpus from its tables and compare what comes back.

Runs under SharpPy. Built with CODE_TABLE_CHECK, PyCodeObject.FromTables also re-encodes the
instructions it decoded, so a difference there raises here.

usage: sharppy tools/check_code_rebuild.py <directory> ...
"""
import os
import sys
import types


FIELDS = ("co_argcount", "co_posonlyargcount", "co_kwonlyargcount", "co_nlocals", "co_stacksize",
          "co_flags", "co_firstlineno", "co_code", "co_names", "co_varnames", "co_freevars",
          "co_cellvars", "co_filename", "co_name", "co_qualname", "co_linetable",
          "co_exceptiontable")


def rebuild(code):
    consts = tuple(rebuild(c) if isinstance(c, types.CodeType) else c for c in code.co_consts)
    return code.replace(co_consts=consts, co_code=code.co_code)


def same(a, b, path):
    for field in FIELDS:
        if getattr(a, field) != getattr(b, field):
            print("DIFF", path, a.co_name, field)
            return False
    if list(a.co_positions()) != list(b.co_positions()):
        print("DIFF", path, a.co_name, "co_positions")
        return False
    return all(same(x, y, path) for x, y in zip(a.co_consts, b.co_consts)
               if isinstance(x, types.CodeType))


def count(code):
    return 1 + sum(count(c) for c in code.co_consts if isinstance(c, types.CodeType))


files = objects = bad = 0
for top in sys.argv[1:]:
    for folder, _, names in os.walk(top):
        for name in names:
            if not name.endswith(".py"):
                continue
            path = os.path.join(folder, name)
            try:
                with open(path, encoding="utf-8") as stream:
                    code = compile(stream.read(), path, "exec")
            except (SyntaxError, UnicodeDecodeError, ValueError):
                continue
            files += 1
            objects += count(code)
            try:
                if not same(code, rebuild(code), path):
                    bad += 1
            except Exception as error:
                bad += 1
                print("ERROR", path, type(error).__name__, error)
print("files", files, "code objects", objects, "bad", bad)
