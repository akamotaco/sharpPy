"""Compare code-unit exception table presence/coverage with actual CPython 3.12."""
import argparse
import dis
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import types

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--harness', default='tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll')
parser.add_argument('--source', default='tests/class_exception_table_regression.py')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if sys.version_info[:2] != (3, 12):
    parser.error('Requires CPython 3.12')
root = Path(__file__).resolve().parents[1]
source = root / args.source
reference = []

def visit(code, parent=''):
    location = parent + '/' + code.co_name if parent else code.co_name
    entries = [dict(start=e.start, end=e.end, target=e.target, depth=e.depth, lasti=e.lasti)
               for e in dis.Bytecode(code).exception_entries]
    instructions = list(dis.get_instructions(code))
    def covered(op):
        return any(i.opname == op and any(e['start'] <= i.offset < e['end'] for e in entries)
                   for i in instructions)
    reference.append(dict(path=location, table_present=bool(entries),
        offset_conversion_valid=all(i.offset == index * 2 for index, i in
                                    enumerate(dis.get_instructions(code, show_caches=True))),
        bounds_valid=all(0 <= e['start'] < e['end'] <= len(code.co_code) and
                        0 <= e['target'] < len(code.co_code) and e['depth'] >= 0 and
                        e['start'] % 2 == e['end'] % 2 == e['target'] % 2 == 0 for e in entries),
        protected_binary_op=covered('BINARY_OP'), protected_raise=covered('RAISE_VARARGS'),
        code_bytes=len(code.co_code), entries=entries))
    for child in code.co_consts:
        if isinstance(child, types.CodeType):
            visit(child, location)

visit(compile(source.read_text(encoding='utf-8'), str(source), 'exec'))
env = dict(os.environ, DOTNET_ROOT=str(Path(args.dotnet).resolve().parent))
result = subprocess.run([args.dotnet, args.harness, '--exception-table-inspect', str(source)],
                        cwd=root, env=env, capture_output=True, encoding='utf-8', check=True)
actual = json.loads(result.stdout.splitlines()[-1])
fields = ('path', 'table_present', 'bounds_valid', 'offset_conversion_valid', 'protected_binary_op', 'protected_raise')
summarize = lambda rows: {r['path']: {k: r[k] for k in fields} for r in rows}
matching = summarize(reference) == summarize(actual['code_units']) and actual['nonempty_tables_distinct']
report = dict(python=sys.version, scope='Code-unit table presence, valid byte ranges and protected BINARY_OP/RAISE_VARARGS presence; raw layout differences retained, not full table equivalence',
    source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    dll_sha256=hashlib.sha256((root/args.harness).with_name('sharppy.dll').read_bytes()).hexdigest(),
    reference=reference, sharppy=actual, matching=matching)
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
if not matching:
    raise SystemExit('Exception table comparison: FAIL (see report)')
print('Exception table presence/coverage/bounds comparison: PASS')
