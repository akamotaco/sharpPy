"""Compare the AST SharpPy parses with CPython's, statement by statement.

The first stage of the pipeline: tokens to AST. A difference here is a parser or
action route, below every other comparison. Output of `--ast` is matched against
ast.dump, whitespace outside string literals removed.

Usage: python tools/compare_ast.py --dotnet <dotnet> [--show N] [--minimum N] [files]
"""
import argparse
import ast
import collections
import glob
import json
import os
import re
import subprocess
import sys
import warnings

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")


def squeeze(text):
    """Whitespace outside quotes goes; a quoted string stays as it is."""
    out = []
    quote = None
    i = 0
    while i < len(text):
        ch = text[i]
        if quote:
            out.append(ch)
            if ch == "\\":
                i += 1
                out.append(text[i])
            elif ch == quote:
                quote = None
        elif ch in "'\"":
            quote = ch
            out.append(ch)
        elif not ch.isspace():
            out.append(ch)
        i += 1
    return "".join(out)


def split_body(text):
    """Module(body=[A,B,C],type_ignores=[]) into its statements."""
    start = text.find("body=[") + len("body=[")
    depth = 0
    quote = None
    items = []
    begin = start
    i = start
    while i < len(text):
        ch = text[i]
        if quote:
            if ch == "\\":
                i += 1
            elif ch == quote:
                quote = None
        elif ch in "'\"":
            quote = ch
        elif ch in "([{":
            depth += 1
        elif ch in ")}":
            depth -= 1
        elif ch == "]":
            if depth == 0:
                if i > begin:
                    items.append(text[begin:i])
                return items
            depth -= 1
        elif ch == "," and depth == 0:
            items.append(text[begin:i])
            begin = i + 1
        i += 1
    return items


def sharppy_ast(dotnet, path, attributes=False):
    env = dict(os.environ, PYTHONIOENCODING="utf-8", SHARPPY_AST_ATTRIBUTES="1" if attributes else "0")
    run = subprocess.run([dotnet, DLL, "--ast", "--compile", path], capture_output=True, timeout=60,
                         stdin=subprocess.DEVNULL, env=env, cwd=ROOT)
    out = run.stdout.decode("utf-8", "replace").replace("\r\n", "\n")
    match = re.search(r"\[=+ ast log =+\]\n(.*?)\[=+ ast end =+\]", out, re.S)
    return match.group(1) if match else None


def first_difference(a, b):
    i = 0
    n = min(len(a), len(b))
    while i < n and a[i] == b[i]:
        i += 1
    return i


NODE = re.compile(r"[A-Za-z_]+\(?=?")


def signature(text, at):
    """A key for the tally: the node and field around the first difference."""
    before = text[max(0, at - 30):at]
    names = re.findall(r"[A-Za-z_]+", before)
    return names[-1] if names else ""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--show", type=int, default=20)
    parser.add_argument("--minimum", type=int, default=0)
    parser.add_argument("--attributes", action="store_true", help="compare line and column too")
    parser.add_argument("files", nargs="*")
    args = parser.parse_args()
    files = args.files or sorted(glob.glob(os.path.join(ROOT, "tests", "bughunt", "*.py")))

    total = same = failed = 0
    tally = collections.Counter()
    sample = {}
    for path in files:
        try:
            source = open(path, encoding="utf-8").read()
            with warnings.catch_warnings():
                warnings.simplefilter("ignore")
                theirs = split_body(squeeze(ast.dump(ast.parse(source), include_attributes=args.attributes)))
            produced = sharppy_ast(args.dotnet, path, args.attributes)
        except (SyntaxError, ValueError, RecursionError, subprocess.TimeoutExpired):
            failed += 1
            continue
        if produced is None:
            print("no tree:", path)
            failed += 1
            continue
        mine = split_body(squeeze(produced))
        total += len(theirs)
        for index, reference in enumerate(theirs):
            statement = mine[index] if index < len(mine) else ""
            if statement == reference:
                same += 1
                continue
            at = first_difference(reference, statement)
            key = (reference[max(0, at - 24):at + 16], statement[max(0, at - 24):at + 16])
            short = (signature(reference, at), reference[at:at + 18], statement[at:at + 18])
            tally[short] += 1
            sample.setdefault(short, key)

    print("=== most frequent differences (near, CPython -> SharpPy) ===")
    for key, count in tally.most_common(args.show):
        print(f"{count:6} {key[0][:18]:18} {key[1]:20} -> {key[2]}")
    print(json.dumps(dict(statements=total, identical=same, different=total - same, files_failed=failed)))
    return 1 if same < args.minimum else 0


if __name__ == "__main__":
    sys.exit(main())
