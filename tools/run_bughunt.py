"""Run tests/bughunt probes under SharpPy and reference CPython 3.12, diff outputs.

Must be executed with the reference CPython 3.12 interpreter.
Exit code: number of mismatching probes.
"""
import argparse
import difflib
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(cmd, cwd, timeout, env=None):
    try:
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, timeout=timeout, env=env)
        out = p.stdout.decode("utf-8", errors="replace").replace("\r\n", "\n")
        err = p.stderr.decode("utf-8", errors="replace").replace("\r\n", "\n")
        return p.returncode, out, err
    except subprocess.TimeoutExpired:
        return "TIMEOUT", "", ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dotnet", default=r"C:\Users\AMD-MAX395-128G-2T\AppData\Local\SharpPy\dotnet\dotnet.exe")
    ap.add_argument("--dll", default=str(ROOT / "bin" / "Release" / "net8.0" / "sharppy.dll"))
    ap.add_argument("--probes", default=str(ROOT / "tests" / "bughunt"))
    ap.add_argument("--keep", default=str(ROOT / ".artifacts" / "bughunt"))
    ap.add_argument("--timeout", type=int, default=60)
    ap.add_argument("--filter", default=None)
    args = ap.parse_args()

    probe_dir = Path(args.probes)
    keep = Path(args.keep)
    keep.mkdir(parents=True, exist_ok=True)
    files = sorted(probe_dir.rglob("*.py"))
    if args.filter:
        files = [f for f in files if args.filter in str(f)]

    # One shared scratch dir so probes never write into the repo root and both
    # interpreters see identical absolute paths in messages.
    scratch = ROOT / ".artifacts" / "bughunt-scratch"
    scratch.mkdir(parents=True, exist_ok=True)
    for stale in scratch.iterdir():
        try:
            stale.unlink()
        except OSError:
            pass  # e.g. a file an abandoned SharpPy run still holds open
    env = dict(os.environ, BUGHUNT_SCRATCH=str(scratch))

    mismatches = []
    for f in files:
        rel = f.relative_to(ROOT).as_posix()
        rc_c, out_c, err_c = run([sys.executable, rel], ROOT, args.timeout, env)
        rc_s, out_s, err_s = run([args.dotnet, args.dll, rel], ROOT, args.timeout, env)
        (keep / (f.stem + ".cp.txt")).write_text(out_c + "---STDERR---\n" + err_c, encoding="utf-8")
        (keep / (f.stem + ".sp.txt")).write_text(out_s + "---STDERR---\n" + err_s, encoding="utf-8")
        ok = (rc_c == rc_s) and (out_c == out_s)
        # stderr: report but only count first line + last line differences (tracebacks contain paths)
        if not ok:
            mismatches.append(rel)
            print(f"=== MISMATCH {rel} (rc cp={rc_c} sp={rc_s}) ===")
            for line in list(difflib.unified_diff(
                    (out_c + "---STDERR---\n" + err_c).splitlines(),
                    (out_s + "---STDERR---\n" + err_s).splitlines(),
                    "cpython", "sharppy", lineterm=""))[:60]:
                print(line)
        else:
            print(f"ok {rel}")

    print(f"\n{len(files) - len(mismatches)}/{len(files)} matching; mismatches: {mismatches}")
    return len(mismatches)


if __name__ == "__main__":
    sys.exit(main())
