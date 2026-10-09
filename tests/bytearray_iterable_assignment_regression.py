def values():
    yield 88
    yield 89

for source in [[88, 89], (88, 89), iter([88, 89]), values()]:
    data = bytearray(b'abc')
    data[::2] = source
    assert data == bytearray(b'XbY')

data = bytearray(b'abc')
data[1:2] = [88, 89]
assert data == bytearray(b'aXYc')
data[::-2] = []
assert data == bytearray(b'aY')
data.extend(iter([90, 91]))
assert data == bytearray(b'aYZ[')

class Source:
    def __init__(self, failure):
        self.position = 0
        self.failure = failure
    def __iter__(self):
        return self
    def __next__(self):
        self.position += 1
        if self.position == 1:
            return 88
        if self.failure:
            raise RuntimeError('source failed')
        raise StopIteration('finished')

source = Source(False)
data = bytearray(b'abc')
data[1:2] = source
assert data == bytearray(b'aXc')
assert source.position == 2

source = Source(True)
data = bytearray(b'abc')
try:
    data[:] = source
except RuntimeError as error:
    assert str(error) == 'source failed'
else:
    raise AssertionError('expected source error')
assert data == bytearray(b'abc')
assert source.position == 2

for source, message in [([88, 256], 'byte must be in range(0, 256)'),
                        ([88], 'attempt to assign bytes of size 1 to extended slice of size 2')]:
    data = bytearray(b'abc')
    try:
        data[::2] = source
    except ValueError as error:
        assert str(error) == message
    else:
        raise AssertionError('expected ValueError')
    assert data == bytearray(b'abc')

data = bytearray(b'abc')
view = memoryview(data)
data[::2] = [88, 89]
assert [view[0], view[1], view[2]] == [88, 98, 89]
try:
    data[:] = []
except BufferError:
    pass
else:
    raise AssertionError('expected BufferError')
assert data == bytearray(b'XbY')
view.release()
print('bytearray iterable assignment: PASS')
