#!/usr/bin/env python3
"""Compare sys.argv and command-mode behaviour between CPython and SharpPy.

The bughunt harness can only run scripts, and the interesting part of sys.argv is the command line
that launched the interpreter, so this tool drives both engines through the shapes measured from
CPython 3.12.15 (Python/initconfig.c:2723 config_update_argv).

Exit code is the number of mismatching cases.
"""

import argparse
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

PROBE = (
    "import sys\n"
    "print('argv', sys.argv)\n"
    "print('count', len(sys.argv))\n"
)

# (label, argv-tail after the engine, stdin text or None, is_expected_gap)
CASES = [
    ("script with args", ["{script}", "one", "two"], None, False),
    ("script no args", ["{script}"], None, False),
    ("interpreter flag before script", ["-u", "{script}", "x"], None, False),
    ("path with backslashes", ["{script_bs}", "z"], None, False),
    ("-c code", ["-c", "import sys; print('argv', sys.argv); print('count', len(sys.argv))"], None, False),
    ("-c code with args", ["-c",
                           "import sys; print('argv', sys.argv); print('count', len(sys.argv))",
                           "one", "two"], None, False),
    ("-c empty program", ["-c", ""], None, False),
    ("dash reads stdin", ["-", "one"], PROBE, True),
    ("bare interpreter on stdin", [], PROBE, True),
    ("-c without operand", ["-c"], None, True),
]


def run(cmd, cwd, stdin=None):
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", cwd=cwd, input=stdin, timeout=120)
    out = (proc.stdout or "").replace("\r", "").strip()
    err = (proc.stderr or "").replace("\r", "").strip()
    return proc.returncode, out, err


def normalise(text, script):
    """Strip engine-specific program name noise, keep what the program printed."""
    lines = []
    for line in text.split("\n"):
        if line.startswith("**") or "REPL" in line or line.startswith(">>>"):
            continue
        lines.append(line.replace(script, "<script>"))
    return "\n".join(lines).strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--python", default=sys.executable)
    ap.add_argument("--dotnet", required=True)
    ap.add_argument("--dll", default=os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll"))
    args = ap.parse_args()

    scratch = os.path.join(ROOT, ".artifacts", "tmp", "argv_matrix")
    os.makedirs(scratch, exist_ok=True)
    script_rel = os.path.join(".artifacts", "tmp", "argv_matrix", "argv_subject.py")
    with open(os.path.join(ROOT, script_rel), "w", encoding="utf-8") as fh:
        fh.write(PROBE)
    script_bs = script_rel.replace("/", "\\")

    diffs = 0
    gaps = 0
    for label, tail, stdin, expected_gap in CASES:
        def expand(t):
            return t.replace("{script}", script_rel).replace("{script_bs}", script_bs)

        cpy_cmd = [args.python] + [expand(t) for t in tail]
        spy_cmd = [args.dotnet, args.dll] + [expand(t) for t in tail]
        rc_c, out_c, err_c = run(cpy_cmd, ROOT, stdin)
        rc_s, out_s, err_s = run(spy_cmd, ROOT, stdin)
        body_c = normalise(out_c, script_rel)
        body_s = normalise(out_s, script_rel)
        same = (rc_c == rc_s) and body_c == body_s
        tag = "OK  " if same else ("GAP " if expected_gap else "DIFF")
        if not same:
            gaps += 1 if expected_gap else 0
            diffs += 0 if expected_gap else 1
        print("%s %-34s rc %s/%s" % (tag, label, rc_c, rc_s))
        if not same:
            print("     cpython: %s" % (body_c or err_c).replace("\n", " | ")[:120])
            print("     sharppy: %s" % (body_s or err_s).replace("\n", " | ")[:120])

    print("mismatches: %d (%d registered gaps excluded)" % (diffs, gaps))
    return diffs


if __name__ == "__main__":
    sys.exit(main())
