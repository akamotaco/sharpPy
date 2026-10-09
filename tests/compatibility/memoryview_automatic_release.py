"""Known gap: losing the last reference should release the exporter in CPython."""
data = bytearray(b'abc')
view = memoryview(data)
del view
data.append(ord('d'))
assert data == bytearray(b'abcd')
print('memoryview automatic release: PASS')
