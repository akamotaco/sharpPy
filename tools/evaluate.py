"""Run existing suites in isolated processes; retain outputs and explicit failures.

Requires CPython 3.12. Timing here is diagnostic process duration, not a VM benchmark.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]


def failure_markers(output):
    markers = []
    for line in output.splitlines():
        if re.search(r"(?:Failed:|FAIL(?:ED)?\s*[:=])\s*[1-9]\d*|[1-9]\d*\s+(?:FAILED|test\(s\) failed)", line, re.I):
            markers.append(line)
        elif (re.search(r"^\s*(?:FAIL:|FAILED:)", line) or line.startswith("❌")) and not re.search(r"Failed:\s*0\b", line, re.I):
            markers.append(line)
    return markers


def run(command, timeout, env):
    start = time.perf_counter()
    try:
        process = subprocess.run(command, cwd=ROOT, env=env, capture_output=True,
                                 encoding="utf-8", errors="replace", timeout=timeout)
        result = dict(exit_code=process.returncode, stdout=process.stdout,
                      stderr=process.stderr, timed_out=False)
    except subprocess.TimeoutExpired as error:
        def decode(value):
            return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else value or ""
        result = dict(exit_code=None, stdout=decode(error.stdout), stderr=decode(error.stderr), timed_out=True)
    result["process_seconds"] = time.perf_counter() - start
    result["failure_markers"] = failure_markers(result["stdout"] + "\n" + result["stderr"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--dll", default="bin/Release/net8.0/sharppy.dll")
    parser.add_argument("--output", default=".artifacts/evaluation/baseline.json")
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--files", nargs="*")
    args = parser.parse_args()
    if sys.version_info[:2] != (3, 12):
        parser.error("Run this evaluator with the reference CPython 3.12 interpreter")
    files = args.files if args.files is not None else [
        *[str(path.relative_to(ROOT)) for path in sorted((ROOT / "tests").glob("level*.py"))],
        *[str(path.relative_to(ROOT)) for path in sorted(ROOT.glob("test_*.py"))],
        "tests/binary_inplace_regression.py",
        "tests/inplace_special_lookup.py",
        "tests/function_creation_regression.py",
        "examples/hello.py", "examples/advanced.py",
    ]
    env = dict(os.environ, PYTHONUTF8="1", PYTHONHASHSEED="0")
    env["DOTNET_ROOT"] = str(Path(args.dotnet).resolve().parent)
    report = dict(python=sys.version, executable=sys.executable, dotnet=str(Path(args.dotnet).resolve()),
                  dotnet_info=run([args.dotnet, "--info"], args.timeout, env),
                  git=run(["git", "rev-parse", "HEAD"], args.timeout, env)["stdout"].strip(),
                  git_status=run(["git", "status", "--short"], args.timeout, env)["stdout"],
                  dll_sha256=hashlib.sha256((ROOT / args.dll).read_bytes()).hexdigest(),
                  timeout=args.timeout, cases=[])
    output = ROOT / args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    commands = [(name, [sys.executable, name], [args.dotnet, args.dll, name]) for name in files]
    if args.files is None:
        commands += [(flag, None, [args.dotnet, args.dll, flag]) for flag in
                     ("--test-generator-kwargs", "--test-pymethod-kwargs")]
    for name, reference_command, sharppy_command in commands:
        reference = run(reference_command, args.timeout, env) if reference_command else None
        sharppy = run(sharppy_command, args.timeout, env)
        if sharppy["timed_out"]:
            status = "sharppy_timeout"
        elif reference and reference["timed_out"]:
            status = "reference_timeout"
        elif reference and (reference["exit_code"] != 0 or reference["failure_markers"]):
            status = "reference_test_failure"
        elif sharppy["exit_code"] != 0 or sharppy["failure_markers"]:
            status = "sharppy_failure"
        elif reference and (reference["stdout"] != sharppy["stdout"] or reference["stderr"] != sharppy["stderr"]):
            status = "output_difference"
        else:
            status = "matching_output" if reference else "no_reported_failure"
        report["cases"].append(dict(name=name, status=status, reference=reference, sharppy=sharppy))
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{name}: {status}", flush=True)
    counts = {}
    for case in report["cases"]:
        counts[case["status"]] = counts.get(case["status"], 0) + 1
    report["counts"] = counts
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(counts, ensure_ascii=False))
    return int(any(case["status"] in ("sharppy_timeout", "sharppy_failure", "reference_timeout", "reference_test_failure")
                   for case in report["cases"]))


if __name__ == "__main__":
    raise SystemExit(main())
