"""Stream all scalar/surrogate repr results from the required CPython 3.12 oracle."""
import sys
from pathlib import Path
if sys.implementation.name != 'cpython' or sys.version_info[:2] != (3, 12):
    raise RuntimeError('CPython 3.12 is required')
with Path(sys.argv[1]).open('w', encoding='utf-8', newline='\n') as output:
    for point in range(0x110000):
        output.write(repr(chr(point)) + '\n')
print(sys.version)
