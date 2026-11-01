#!/usr/bin/env python3
"""Compare the symbol table of each scope with CPython's, through the _symtable module.

Flags are compared bit for bit, with order of ste_symbols ignored for names that
update_symbols adds from a set. `_symtable.symtable` gives the raw entry tree.
"""
import argparse
import glob
import json
import os
import subprocess
import sys

import _symtable


def entry_text(ste, prefix, out):
    symbols = sorted((name, flags) for name, flags in ste.symbols.items())
    out.append(f"{prefix}{ste.name}:{ste.type}:{ste.lineno}:{int(ste.nested)} vars={','.join(ste.varnames)} "
               + " ".join(f"{name}={flags}" for name, flags in symbols))
    for child in ste.children:
        entry_text(child, prefix + " ", out)


def cpython_table(source, path):
    out = []
    entry_text(_symtable.symtable(source, path, "exec"), "", out)
    return out


def sharppy_table(dotnet, path):
    env = dict(os.environ, SHARPPY_SYMTABLE="1")
    done = subprocess.run([dotnet, "bin/Release/net8.0/sharppy.dll", "--ast", "--compile", path],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    lines = done.stdout.splitlines()
    end = lines.index("[===== ast log ====]") if "[===== ast log ====]" in lines else 0
    return [line for line in lines[:end] if line], done.stderr


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--show", type=int, default=5)
    parser.add_argument("files", nargs="*")
    args = parser.parse_args()
    files = args.files or sorted(glob.glob("tests/bughunt/*.py"))
    scopes = same = failed = 0
    shown = 0
    for path in files:
        try:
            source = open(path, encoding="utf-8").read()
            theirs = cpython_table(source, path)
        except (SyntaxError, ValueError, UnicodeDecodeError):
            continue
        mine, errors = sharppy_table(args.dotnet, path)
        if not mine:
            failed += 1
            print(path, "no output", errors[:300], file=sys.stderr)
            continue
        scopes += len(theirs)
        for index, line in enumerate(theirs):
            other = mine[index] if index < len(mine) else ""
            if line == other:
                same += 1
            elif shown < args.show:
                shown += 1
                print(path)
                print("  C", line[:400])
                print("  S", other[:400])
    print(json.dumps({"scopes": scopes, "identical": same, "files_failed": failed}))


if __name__ == "__main__":
    main()
