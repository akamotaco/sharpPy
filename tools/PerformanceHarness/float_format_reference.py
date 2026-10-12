"""Generate reproducible binary64 formatting cases with CPython 3.12."""
import json
import random
import struct
import sys
from pathlib import Path

if sys.implementation.name != 'cpython' or sys.version_info[:2] != (3, 12):
    raise RuntimeError('CPython 3.12 is required')
rng = random.Random(124)
specs = ('', '.0', '.1', '.6', '.17', 'g', '#g', '.0g', '.17g', '#.4g',
         '.0e', '+020.6E', '#.0f', '.17f', '%', 'z.2f', '030,.2f', '025_.6g')
with Path(sys.argv[1]).open('w', encoding='utf-8', newline='\n') as output:
    for index in range(1500):
        bits = rng.getrandbits(64)
        value = struct.unpack('>d', bits.to_bytes(8, 'big'))[0]
        for spec in specs:
            output.write(json.dumps(dict(bits=f'{bits:016x}', spec=spec, expected=format(value, spec))) + '\n')
print(sys.version)
