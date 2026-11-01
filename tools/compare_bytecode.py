"""Compare the bytecode SharpPy emits with CPython's, code object by code object.

Output comparison cannot see a wrong route that happens to print the right thing.
Four levels, each one a separate count:
  names       instruction names (CACHE left out)
  arguments   each argument as dis shows it: the name, the constant, the jump target
  lines       the source line of each instruction
  exceptions  the exception table

Usage: python tools/compare_bytecode.py --dotnet <dotnet> [--show N] [--level L]
                                        [--minimum N] [files]
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

NAME_OPS = {"LOAD_NAME", "STORE_NAME", "DELETE_NAME", "STORE_GLOBAL", "DELETE_GLOBAL", "STORE_ATTR", "DELETE_ATTR",
            "IMPORT_NAME", "IMPORT_FROM", "LOAD_FROM_DICT_OR_GLOBALS"}
FLAGGED_OPS = {"LOAD_GLOBAL", "LOAD_ATTR", "LOAD_SUPER_ATTR"}
LOCAL_OPS = {"LOAD_FAST", "STORE_FAST", "DELETE_FAST", "LOAD_FAST_CHECK", "LOAD_FAST_AND_CLEAR", "LOAD_DEREF",
             "STORE_DEREF", "DELETE_DEREF", "LOAD_CLOSURE", "MAKE_CELL", "LOAD_FROM_DICT_OR_DEREF"}
JUMP_OPS = {"JUMP_FORWARD", "POP_JUMP_IF_TRUE", "POP_JUMP_IF_FALSE", "POP_JUMP_IF_NONE", "POP_JUMP_IF_NOT_NONE",
            "FOR_ITER", "SEND", "JUMP_BACKWARD", "JUMP_BACKWARD_NO_INTERRUPT"}
COMPARE_NAMES = {"<": "LT", "<=": "LE", "==": "EQ", "!=": "NE", ">": "GT", ">=": "GE"}
ZERO_ARG = {"NOP", "POP_TOP", "RETURN_VALUE", "PUSH_NULL", "END_FOR", "GET_ITER", "BINARY_SUBSCR", "STORE_SUBSCR"}


def plain(name):
    match = re.fullmatch(r"<class_body_(.*)>", name)
    if match:
        return match.group(1)
    return "<lambda>" if re.fullmatch(r"<lambda_\d+>", name) else name


class Unit:
    def __init__(self):
        self.ops = []
        self.details = []
        self.lines = []
        self.table = []


def constant_detail(value):
    if isinstance(value, types.CodeType):
        return "<code %s>" % value.co_name
    if isinstance(value, frozenset):
        return "<frozenset>"
    return repr(value).replace("\n", "\\n").replace("\t", "\\t")


def detail(i):
    op = i.opname
    if op in ("LOAD_CONST", "RETURN_CONST", "KW_NAMES"):
        return constant_detail(i.argval)
    if op in NAME_OPS:
        return str(i.argval)
    if op == "LOAD_SUPER_ATTR":
        return "%s|%d" % (i.argval, i.arg & 3)
    if op in FLAGGED_OPS:
        return "%s|%d" % (i.argval, i.arg & 1)
    if op in LOCAL_OPS:
        return str(i.argval)
    if op in JUMP_OPS:
        return str(i.argval)
    if op == "COMPARE_OP":
        return COMPARE_NAMES.get(i.argrepr, i.argrepr)
    return str(i.arg if i.arg is not None else 0)


def cpython_code(source, path):
    found = collections.defaultdict(list)

    def walk(code):
        unit = Unit()
        for i in dis.get_instructions(code):
            if i.opname == "EXTENDED_ARG":
                continue
            unit.ops.append(i.opname)
            unit.details.append(detail(i))
            line = i.positions.lineno if i.positions else None
            unit.lines.append(line if line is not None else -1)
        unit.table = [(e.start, e.end, e.target, e.depth, 1 if e.lasti else 0) for e in dis._parse_exception_table(code)]
        found[code.co_name].append(unit)
        for const in code.co_consts:
            if isinstance(const, types.CodeType):
                walk(const)

    walk(compile(source, path, "exec"))
    return found


def sharppy_code(dotnet, path):
    env = dict(os.environ, PYTHONIOENCODING="utf-8", SHARPPY_DIS_FULL="1")
    run = subprocess.run([dotnet, DLL, "--dis", "--compile", path], capture_output=True, timeout=60,
                         stdin=subprocess.DEVNULL, env=env, cwd=ROOT)
    found = collections.defaultdict(list)
    unit = None
    for line in run.stdout.decode("utf-8", "replace").replace("\r\n", "\n").split("\n"):
        if line.startswith("@code "):
            unit = Unit()
            found[plain(line[6:])].append(unit)
        elif line.startswith("#") and unit is not None:
            parts = line.split("\t")
            if len(parts) == 4:
                unit.ops.append(parts[1])
                unit.details.append(parts[2])
                unit.lines.append(int(parts[3]))
        elif line.startswith("@exc\t") and unit is not None:
            unit.table.append(tuple(int(x) for x in line.split("\t")[1:]))
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
    parser.add_argument("--level", choices=("names", "arguments", "lines", "exceptions"), default="names",
                        help="which level the list of differences shows")
    parser.add_argument("--minimum", type=int, default=0, help="fail below this many identical code objects by name")
    parser.add_argument("files", nargs="*")
    args = parser.parse_args()
    files = args.files or sorted(glob.glob(os.path.join(ROOT, "tests", "bughunt", "*.py")))

    count = collections.Counter()
    tally = collections.Counter()
    for path in files:
        try:
            source = open(path, encoding="utf-8").read()
            theirs = cpython_code(source, path)
            mine = sharppy_code(args.dotnet, path)
        except (SyntaxError, ValueError, UnicodeDecodeError, subprocess.TimeoutExpired):
            count["files_failed"] += 1
            continue
        for name, reference in theirs.items():
            produced = mine.get(name, [])
            for index, unit in enumerate(reference):
                count["code_objects"] += 1
                if index >= len(produced):
                    count["missing"] += 1
                    continue
                other = produced[index]
                if other.ops != unit.ops:
                    if args.level == "names":
                        differences(other.ops, unit.ops, tally)
                    continue
                count["names"] += 1
                if other.details == unit.details:
                    count["arguments"] += 1
                elif args.level == "arguments":
                    for op, a, b in zip(unit.ops, unit.details, other.details):
                        if a != b:
                            kind = op if op in JUMP_OPS or op in LOCAL_OPS or op in NAME_OPS or op in FLAGGED_OPS else op + " " + a[:24]
                            tally[(kind, b[:24] if kind == op + " " + a[:24] else "%s -> %s" % (a[:20], b[:20]))] += 1
                            break
                if other.lines == unit.lines:
                    count["lines"] += 1
                elif args.level == "lines":
                    for op, a, b in zip(unit.ops, unit.lines, other.lines):
                        if a != b:
                            tally[(op, "%+d" % (b - a) if a > 0 and b > 0 else "%d -> %d" % (min(a, 0), min(b, 0)))] += 1
                            break
                if other.table == unit.table:
                    count["exceptions"] += 1
                elif args.level == "exceptions":
                    tally[(str(len(unit.table)), str(len(other.table)))] += 1

    print("=== most frequent differences (CPython -> SharpPy), level %s ===" % args.level)
    for (theirs, mine), n in tally.most_common(args.show):
        print(f"{n:6} {theirs[:64]:64} -> {mine[:64]}")
    print(json.dumps({key: count[key] for key in ("code_objects", "names", "arguments", "lines", "exceptions", "missing", "files_failed")}))
    return 1 if count["names"] < args.minimum else 0


if __name__ == "__main__":
    sys.exit(main())
