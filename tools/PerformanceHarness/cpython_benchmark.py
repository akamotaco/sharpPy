"""Reference steady state timing; uses exactly the same workloads as the C# host."""
import argparse
import json
from pathlib import Path
import sys
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--iterations", type=int, default=10000)
parser.add_argument("--samples", type=int, default=10)
args = parser.parse_args()
if sys.version_info[:2] != (3, 12) or min(args.iterations, args.samples) < 1:
    parser.error("Requires CPython 3.12 and positive iteration/sample counts")
namespace = {}
start = time.perf_counter()
exec(compile(Path(__file__).with_name("workloads.py").read_text(encoding="utf-8"), "workloads.py", "exec"), namespace)
setup_ms = (time.perf_counter() - start) * 1000
results = []
expected_sum = args.iterations * (args.iterations - 1) // 2
for name in ("addition", "inplace_addition", "arithmetic", "function_call", "attributes", "dictionary", "dictionary_iteration", "dictionary_copy", "namespace_update", "namespace_read", "comprehension", "bound_builtin", "locals_observation", "frame_observation", "class_locals_observation", "exception_handling"):
    function = namespace[name]
    expected = expected_sum - args.iterations if name == "arithmetic" else expected_sum
    for _ in range(10):
        assert function(args.iterations) == expected, name
    durations = []
    for _ in range(args.samples):
        start = time.perf_counter()
        result = function(args.iterations)
        durations.append((time.perf_counter() - start) * 1000)
        assert result == expected, name
    results.append(dict(name=name, expected=expected, milliseconds=durations))
print(json.dumps(dict(python=sys.version, iterations=args.iterations, samples=args.samples,
                     warmups=10, setup_milliseconds=setup_ms, results=results)))
