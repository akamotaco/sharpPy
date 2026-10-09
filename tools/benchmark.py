"""Record independent, alternating SharpPy/CPython steady-state benchmark runs.

Build SharpPy and tools/PerformanceHarness in Release before running this script.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--processes", type=int, default=3)
    parser.add_argument("--iterations", type=int, default=10000)
    parser.add_argument("--samples", type=int, default=10)
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args()
    if sys.version_info[:2] != (3, 12) or min(args.processes, args.iterations, args.samples) < 1:
        parser.error("Requires CPython 3.12 and positive counts")
    env = dict(os.environ, PYTHONUTF8="1", PYTHONHASHSEED="0")
    env["DOTNET_ROOT"] = str(Path(args.dotnet).resolve().parent)
    def execute(command):
        return subprocess.run(command, cwd=ROOT, env=env, capture_output=True, encoding="utf-8",
                              errors="strict", timeout=args.timeout, check=True).stdout
    dll = ROOT / "bin/Release/net8.0/sharppy.dll"
    workloads = ROOT / "tools/PerformanceHarness/workloads.py"
    report = dict(created_utc=datetime.now(timezone.utc).isoformat(), python=sys.version,
                  python_executable=sys.executable, platform=platform.platform(), processor=platform.processor(),
                  git=execute(["git", "rev-parse", "HEAD"]).strip(),
                  git_status=execute(["git", "status", "--short"]),
                  dotnet_info=execute([args.dotnet, "--info"]),
                  dll_sha256=hashlib.sha256(dll.read_bytes()).hexdigest(),
                  workloads_sha256=hashlib.sha256(workloads.read_bytes()).hexdigest(),
                  iterations=args.iterations, samples=args.samples, processes=args.processes, runs=[])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    commands = {
        "sharppy": [args.dotnet, "tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll",
                    str(workloads), str(args.iterations), str(args.samples)],
        "cpython": [sys.executable, "tools/PerformanceHarness/cpython_benchmark.py",
                    "--iterations", str(args.iterations), "--samples", str(args.samples)],
    }
    for process in range(args.processes):
        order = ("sharppy", "cpython") if process % 2 == 0 else ("cpython", "sharppy")
        for engine in order:
            result = json.loads(execute(commands[engine]))
            report["runs"].append(dict(engine=engine, process=process + 1, result=result))
            args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            print(f"{engine} process {process + 1}: correct results recorded", flush=True)


if __name__ == "__main__":
    main()
