# Regression: iterator exhaustion must finish bytearray slice conversion.
data = bytearray(b'abc')
data[::2] = [88, 89]
assert data == bytearray(b'XbY')
print('bytearray iterable slice assignment: PASS')
