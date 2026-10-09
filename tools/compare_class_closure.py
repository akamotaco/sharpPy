"""Compare selected class closure metadata/opcodes with actual CPython 3.12."""
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
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if sys.version_info[:2] != (3, 12):
    parser.error('Requires CPython 3.12')
root = Path(__file__).resolve().parents[1]
source = root / 'tests/class_closure_mapping_regression.py'
module = compile(source.read_text(encoding='utf-8'), str(source), 'exec')
reference = []
for name in ('observed', 'local_name', 'prepared'):
    parent = next(c for c in module.co_consts if isinstance(c, types.CodeType) and c.co_name == name)
    body = next(c for c in parent.co_consts if isinstance(c, types.CodeType) and c.co_name == 'Body')
    instructions = list(dis.get_instructions(body))
    pairs = [[a.opcode, a.arg or 0, b.opcode, b.arg or 0]
             for a, b in zip(instructions, instructions[1:])
             if a.opname == 'LOAD_LOCALS' and b.opname == 'LOAD_FROM_DICT_OR_DEREF']
    reference.append(dict(name=name, parent_cells=list(parent.co_cellvars),
                          class_locals=list(body.co_varnames), class_cells=list(body.co_cellvars),
                          class_freevars=list(body.co_freevars), pairs=pairs))
env = dict(os.environ, DOTNET_ROOT=str(Path(args.dotnet).resolve().parent))
command = [args.dotnet, args.harness, '--class-closure-inspect', str(source)]
result = subprocess.run(command, cwd=root, env=env, capture_output=True, encoding='utf-8', check=True)
actual = json.loads(result.stdout.splitlines()[-1])
report = dict(python=sys.version, scope='Three selected class bodies; closure metadata and LOAD_LOCALS/LOAD_FROM_DICT_OR_DEREF pairs, not full bytecode equivalence',
              source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
              dll_sha256=hashlib.sha256((root/args.harness).with_name('sharppy.dll').read_bytes()).hexdigest(),
              reference=reference, sharppy=actual, matching=reference == actual)
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
if reference != actual:
    raise SystemExit('Class closure metadata/opcode comparison: FAIL (see report)')
print('Class closure metadata/opcode comparison: PASS')
