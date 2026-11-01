"""Compare the bytecode SharpPy emits with CPython's, code object by code object.

Output comparison cannot see a wrong route that happens to print the right thing.
This compares instruction names per code object (CACHE entries left out), and is
the mechanical half of the "single route check".

Usage: python tools/compare_bytecode.py --dotnet <dotnet> [--show N] [--minimum N] [files]
Run with the CPython 3.12 used for the other gates.
"""
import argparse
import collections
import difflib
import dis
import glob
import json
import os
import re
import subprocess
import sys
import types

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")
LINE = re.compile(r"^\s*\d+: ([A-Z][A-Z0-9_]*)")
HEADER = re.compile(r"Disassembly of (.*):\s*$")


def plain(name):
    match = re.fullmatch(r"<class_body_(.*)>", name)
    if match:
        return match.group(1)
    return "<lambda>" if re.fullmatch(r"<lambda_\d+>", name) else name


def cpython_code(source, path):
    found = collections.defaultdict(list)

    def walk(code):
        found[code.co_name].append([i.opname for i in dis.get_instructions(code)])
        for const in code.co_consts:
            if isinstance(const, types.CodeType):
                walk(const)

    walk(compile(source, path, "exec"))
    return found


def sharppy_code(dotnet, path):
    env = dict(os.environ, PYTHONIOENCODING="utf-8")
    run = subprocess.run([dotnet, DLL, "--dis", "--compile", path], capture_output=True, timeout=60,
                         stdin=subprocess.DEVNULL, env=env, cwd=ROOT)
    found = collections.defaultdict(list)
    current = None
    for line in run.stdout.decode("utf-8", "replace").splitlines():
        header = HEADER.search(line)
        if header:
            current = []
            found[plain(header.group(1))].append(current)
            continue
        match = LINE.match(line)
        if match and current is not None and match.group(1) != "CACHE":
            current.append(match.group(1))
    return found


def differences(mine, theirs, tally):
    matcher = difflib.SequenceMatcher(a=theirs, b=mine, autojunk=False)
    for tag, a0, a1, b0, b1 in matcher.get_opcodes():
        if tag != "equal":
            tally[(" ".join(theirs[a0:a1]) or "-", " ".join(mine[b0:b1]) or "-")] += 1


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--show", type=int, default=25)
    parser.add_argument("--minimum", type=int, default=0, help="fail below this many identical code objects")
    parser.add_argument("files", nargs="*")
    args = parser.parse_args()
    files = args.files or sorted(glob.glob(os.path.join(ROOT, "tests", "bughunt", "*.py")))

    total = identical = missing = failed = 0
    tally = collections.Counter()
    for path in files:
        try:
            source = open(path, encoding="utf-8").read()
            theirs = cpython_code(source, path)
            mine = sharppy_code(args.dotnet, path)
        except (SyntaxError, subprocess.TimeoutExpired, UnicodeDecodeError):
            failed += 1
            continue
        for name, reference in theirs.items():
            produced = mine.get(name, [])
            for index, ops in enumerate(reference):
                total += 1
                if index >= len(produced):
                    missing += 1
                elif produced[index] == ops:
                    identical += 1
                else:
                    differences(produced[index], ops, tally)

    print("=== most frequent differences (CPython -> SharpPy) ===")
    for (theirs, mine), count in tally.most_common(args.show):
        print(f"{count:6} {theirs[:64]:64} -> {mine[:64]}")
    print(json.dumps(dict(code_objects=total, identical=identical, different=total - identical - missing,
                          missing=missing, files_failed=failed)))
    return 1 if identical < args.minimum else 0


if __name__ == "__main__":
    sys.exit(main())
