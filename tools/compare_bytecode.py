"""Compare opcode declarations and sample instruction words against CPython 3.12.

This is a diagnostic comparison, not a bytecode compatibility certification.
"""
import argparse
import dis
import json
from pathlib import Path
import re
import subprocess
import sys
import types

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
assert sys.version_info[:2] == (3, 12)
root = Path(__file__).resolve().parents[1]
fixture = root / 'tools/fixtures/bytecode_comparison.py'
cs = (root / 'runtime/PyBytecode.cs').read_text(encoding='utf-8-sig')
enum = cs.split('public enum ByteCodeOp : int', 1)[1].split('}', 1)[0]
sharp = {name: int(number) for name, number in re.findall(r'^\s*(\w+)\s*=\s*(\d+)', enum, re.M)}
reference = {name: value for name, value in dis.opmap.items() if value < 256}
matching = [name for name, value in reference.items() if sharp.get(name) == value]
missing = [name for name in reference if name not in sharp]
different = {name: [value, sharp[name]] for name, value in reference.items()
             if name in sharp and sharp[name] != value}
binary_enum = cs.split('public enum BinaryOpType : byte', 1)[1].split('}', 1)[0]
binary_sharp = {name: int(number) for name, number in re.findall(r'^\s*(\w+)\s*=\s*(\d+)', binary_enum, re.M)}
binary_differences = {}
for number, (name, symbol) in enumerate(dis._nb_ops):
    name = name.removeprefix('NB_').replace('REMAINDER', 'MODULO')
    if binary_sharp.get(name) != number:
        binary_differences[name] = dict(cpython=number, sharppy=binary_sharp.get(name), symbol=symbol)
process = subprocess.run([args.dotnet, str(root / 'tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll'),
                          '--bytecode-inspect', str(fixture)], check=True, capture_output=True, encoding='utf-8')
rows = json.loads(process.stdout)
reference_rows = {}
reference_constants = {}
def visit(code, parent=''):
    name = (parent + '/' if parent else '') + code.co_name
    reference_constants[name] = ['<code:' + c.co_name + '>' if isinstance(c, types.CodeType) else repr(c) for c in code.co_consts]
    reference_rows[name] = [dict(opcode=i.opcode, name=i.opname, arg=i.arg or 0)
                            for i in dis.get_instructions(code, show_caches=True, adaptive=False)]
    for child in code.co_consts:
        if isinstance(child, types.CodeType):
            visit(child, name)
visit(compile(fixture.read_text(encoding='utf-8-sig'), str(fixture), 'exec'))
comparisons = []
for row in rows:
    other = reference_rows[row['name']]
    instructions = row['instructions']
    same = [(i['opcode'], i['arg']) for i in instructions] == [(i['opcode'], i['arg']) for i in other]
    def normalize(items, constants):
        return [(i['opcode'], constants[i['arg']] if i['name'] in ('LOAD_CONST', 'RETURN_CONST') else i['arg'])
                for i in items]
    normalized = normalize(instructions, row['constants']) == normalize(other, reference_constants[row['name']])
    comparisons.append(dict(constants_normalized_equal=normalized, sharppy_constants=row['constants'],
                            cpython_constants=reference_constants[row['name']], name=row['name'], same_instruction_words=same,
                            sharppy=instructions, cpython=other))
result = dict(python=sys.version, reference_physical_opcodes=len(reference),
              matching_number_count=len(matching), missing=missing, different_numbers=different,
              sharp_only={name: n for name,n in sharp.items() if n < 256 and name not in reference},
              binary_operand_count=len(dis._nb_ops), binary_operand_differences=binary_differences,
              samples=comparisons)
Path(args.output).write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k != 'samples'}))
for row in comparisons:
    print(row['name'], 'equal' if row['same_instruction_words'] else 'different',
          len(row['cpython']), len(row['sharppy']))
