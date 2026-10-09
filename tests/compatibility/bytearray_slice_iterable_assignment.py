# Outstanding independently of large step normalization: iterator exhaustion.
data = bytearray(b'abc')
data[::2] = [88, 89]
assert data == bytearray(b'XbY')
print('bytearray iterable slice assignment: PASS')
