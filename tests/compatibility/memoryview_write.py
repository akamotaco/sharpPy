# Writes through a view must update the exporter (fixed in round 21).
data = bytearray(b"ABC")
view = memoryview(data)
view[0] = 97
print(data)
assert data == bytearray(b"aBC")
