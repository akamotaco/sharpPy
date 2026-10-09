# Known compatibility finding: writes through a view must update the exporter.
data = bytearray(b"ABC")
view = memoryview(data)
view[0] = 97
print(data)
assert data == bytearray(b"aBC")
