calls = []
class Index:
    def __init__(self, value):
        self.value = value
    def __index__(self):
        calls.append(self.value)
        return self.value

data = bytearray([Index(65), True, False])
assert list(data) == [65, 1, 0]
data.append(Index(66))
data.insert(1, Index(67))
data[0] = Index(68)
data[1:3] = [Index(69), Index(70)]
data.extend([Index(71)])
data.remove(Index(70))
assert list(data) == [68, 69, 0, 66, 71]
assert calls == [65, 66, 67, 68, 69, 70, 71, 70]

for value in [-1, 256, -(10 ** 100), 10 ** 100]:
    for item in [value, Index(value)]:
        data = bytearray(b'abc')
        try:
            data[0] = item
        except ValueError as error:
            assert str(error) == 'byte must be in range(0, 256)'
        else:
            raise AssertionError('expected byte range error')
        assert data == bytearray(b'abc')

for item, message in [(1.5, "'float' object cannot be interpreted as an integer"),
                      ('a', "'str' object cannot be interpreted as an integer"),
                      (Index('a'), '__index__ returned non-int (type str)')]:
    data = bytearray(b'abc')
    try:
        data.append(item)
    except TypeError as error:
        assert str(error) == message
    else:
        raise AssertionError('expected TypeError')
    assert data == bytearray(b'abc')

sentinel = StopIteration('value conversion')
class StopValue:
    def __index__(self):
        raise sentinel
data = bytearray(b'abc')
try:
    data[:] = [65, StopValue()]
except StopIteration as error:
    assert error is sentinel
else:
    raise AssertionError('value StopIteration must not end source iteration')
assert data == bytearray(b'abc')

class Trim:
    def __index__(self):
        del data[-1:]
        return 88
data = bytearray(b'abcdef')
data[-1] = Trim()
assert data == bytearray(b'abcdX')

class Shrink:
    def __index__(self):
        del data[:]
        return 88
data = bytearray(b'abc')
try:
    data[2] = Shrink()
except IndexError as error:
    assert str(error) == 'bytearray index out of range'
else:
    raise AssertionError('expected IndexError after resize')
assert data == bytearray()

data = bytearray(b'abc')
try:
    data[99] = 256
except ValueError as error:
    assert str(error) == 'byte must be in range(0, 256)'
else:
    raise AssertionError('value conversion must precede bounds check')
assert data == bytearray(b'abc')
print('bytearray byte values: PASS')
