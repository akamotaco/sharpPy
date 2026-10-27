"""Per-commit wall-clock regression harness for SharpPy (independent of PerformanceHarness).

Runs .artifacts/perf/bench_suite.py N times under the Release build, records best-of-N
per workload, and (when a baseline exists) prints per-workload deltas.
"""
import argparse, json, os, subprocess, sys, time
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
SUITE = ROOT / ".artifacts/perf/bench_suite.py"


def run(dotnet, dll, reps):
    env = dict(os.environ, PYTHONIOENCODING="utf-8")
    best = {}
    order = []
    for _ in range(reps):
        out = subprocess.run([dotnet, str(dll), str(SUITE)], cwd=ROOT, env=env,
                             capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=300).stdout
        for line in out.splitlines():
            if "\t" in line:
                name, val = line.split("\t")
                val = float(val)
                if name not in best or val < best[name]:
                    best[name] = val
                if name not in order:
                    order.append(name)
    return {n: best[n] for n in order}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--dotnet", required=True)
    p.add_argument("--dll", default="bin/Release/net8.0/sharppy.dll")
    p.add_argument("--reps", type=int, default=3)
    p.add_argument("--label", default="")
    p.add_argument("--save", default=str(ROOT / ".artifacts/perf/latest.json"))
    p.add_argument("--baseline", default=str(ROOT / ".artifacts/perf/baseline.json"))
    a = p.parse_args()
    res = run(a.dotnet, ROOT / a.dll, a.reps)
    git = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    rec = dict(created_utc=datetime.now(timezone.utc).isoformat(), git=git, label=a.label, timings=res)
    Path(a.save).write_text(json.dumps(rec, indent=2), encoding="utf-8")
    bp = Path(a.baseline)
    print(f"{'workload':20s} {'sec':>8s} {'vs base':>9s}")
    if bp.exists():
        base = json.loads(bp.read_text(encoding="utf-8"))["timings"]
        worsen = []
        for n, v in res.items():
            if n in base and base[n] > 0:
                d = (v - base[n]) / base[n] * 100
                print(f"{n:20s} {v:8.4f} {d:+8.1f}%")
                if d > 15:
                    worsen.append((n, d))
            else:
                print(f"{n:20s} {v:8.4f} {'new':>9s}")
        if worsen:
            print("REGRESSION>15%:", worsen)
            sys.exit(2)
    else:
        for n, v in res.items():
            print(f"{n:20s} {v:8.4f}")
        print("(no baseline yet)")


if __name__ == "__main__":
    main()
