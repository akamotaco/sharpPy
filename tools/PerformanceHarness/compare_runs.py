"""Alternate isolated harness processes and retain every sample and DLL hash."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--variant', action='append', required=True, help='label=harness directory')
    parser.add_argument('--workload', action='append', required=True, help='Python file:function')
    parser.add_argument('--iterations', type=int, default=100000)
    parser.add_argument('--samples', type=int, default=15)
    parser.add_argument('--pairs', type=int, default=3)
    parser.add_argument('--affinity', type=lambda value: int(value, 0), help='Windows child process CPU mask')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    if args.affinity is not None and (os.name != 'nt' or args.affinity <= 0):
        parser.error('--affinity requires Windows and a positive CPU mask')
    paths = {label: Path(path).resolve() for label, path in (value.split('=', 1) for value in args.variant)}
    env = dict(os.environ, DOTNET_TieredCompilation='0')
    results = dict(parameters=vars(args), tiered_compilation=False,
                   dll_sha256={label: hashlib.sha256((path / 'sharppy.dll').read_bytes()).hexdigest()
                               for label, path in paths.items()}, runs={})
    for workload in args.workload:
        source, name = workload.rsplit(':', 1)
        runs = {label: [] for label in paths}
        for pair in range(args.pairs):
            for label in (list(paths) if pair % 2 == 0 else list(reversed(paths))):
                command = [args.dotnet, str(paths[label] / 'PerformanceHarness.dll'), source,
                           str(args.iterations), str(args.samples), name]
                with subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                      text=True, encoding='utf-8') as process:
                    if args.affinity is not None:
                        import ctypes
                        setter = ctypes.windll.kernel32.SetProcessAffinityMask
                        setter.argtypes = [ctypes.c_void_p, ctypes.c_size_t]
                        setter.restype = ctypes.c_int
                        if not setter(int(process._handle), args.affinity):
                            process.kill()
                            raise OSError('Could not set child process affinity')
                    try:
                        stdout, stderr = process.communicate(timeout=180)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.communicate()
                        raise
                    if process.returncode:
                        raise subprocess.CalledProcessError(process.returncode, command, stdout, stderr)
                data = json.loads(stdout)
                runs[label].append(data)
                print(name, label, pair + 1, statistics.median(data['results'][0]['milliseconds']), flush=True)
        results['runs'][workload] = runs
        Path(args.output).write_text(json.dumps(results, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
