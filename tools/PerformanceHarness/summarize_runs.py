"""Summarize process medians without treating sample overlap as a speedup."""
import argparse
import json
from pathlib import Path
import statistics


def summarize(data):
    result = {}
    for workload, variants in data['runs'].items():
        result[workload] = {}
        for label, runs in variants.items():
            medians = [statistics.median(run['results'][0]['milliseconds']) for run in runs]
            allocations = [value for run in runs for value in run['results'][0]['allocated_bytes']]
            result[workload][label] = dict(
                processes=len(runs), median_ms=statistics.median(medians),
                process_median_range_ms=[min(medians), max(medians)],
                allocated_bytes_range=[min(allocations), max(allocations)],
            )
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input')
    parser.add_argument('--output')
    args = parser.parse_args()
    text = json.dumps(summarize(json.loads(Path(args.input).read_text(encoding='utf-8'))), indent=2)
    if args.output:
        Path(args.output).write_text(text, encoding='utf-8')
    print(text)
