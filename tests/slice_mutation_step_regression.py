for step in [1, 2, -1, -2, 2147483647, -2147483648, 1073741824, -1073741824]:
    for start, stop in [(0, 6), (1, 6), (5, 0), (5, -1), (2, 2)]:
        if stop == -1 and step > 0:
            continue
        positions = []
        i = start
        while (i < stop if step > 0 else i > stop):
            positions.append(i)
            i += step
        end = None if stop == -1 else stop
        data = bytearray(b'abcdef')
        data[start:end:step] = b'X' * len(positions)
        assert list(data) == [88 if i in positions else 97 + i for i in range(6)]
        data = bytearray(b'abcdef')
        data[start:end:step] = b''
        assert list(data) == [97 + i for i in range(6) if i not in positions]
        for data in [bytearray(b'abcdef'), [97, 98, 99, 100, 101, 102]]:
            del data[start:end:step]
            assert list(data) == [97 + i for i in range(6) if i not in positions]

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value
for data in [bytearray(b'abcdef'), [97, 98, 99, 100, 101, 102]]:
    calls.clear()
    del data[Index('start', 1):Index('stop', 6):Index('step', 2)]
    assert list(data) == [97, 99, 101]
    assert calls == ['step', 'start', 'stop']
data = bytearray(b'abcdef')
data[::-1] = data
assert data == bytearray(b'fedcba')
before = data[:]
try:
    data[::2] = b'x'
except ValueError as error:
    assert str(error) == 'attempt to assign bytes of size 1 to extended slice of size 3'
else:
    raise AssertionError('expected ValueError')
assert data == before
data = bytearray(b'abcdef')
view = memoryview(data)
try:
    data[::2] = b''
except BufferError as error:
    assert str(error) == 'Existing exports of data: object cannot be re-sized'
else:
    raise AssertionError('expected BufferError')
assert data == bytearray(b'abcdef')
view.release()
print('slice mutation steps: PASS')
