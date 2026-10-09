calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value

for source in [b'XY', [88, 89], bytearray(b'XY')]:
    calls.clear()
    data = bytearray(b'abc')
    data[Index('start', 0):Index('stop', 3):Index('step', 2)] = source
    assert data == bytearray(b'XbY')
    assert calls == ['step', 'start', 'stop'] * (1 if isinstance(source, bytearray) else 2)

class GrowingStart:
    def __index__(self):
        calls.append('start')
        data.append(100)
        return 1

for source in [b'XY', bytearray(b'XY')]:
    calls.clear()
    data = bytearray(b'abc')
    data[GrowingStart():] = source
    assert data == bytearray(b'aXY')
    assert len(calls) == (1 if isinstance(source, bytearray) else 2)

calls.clear()
data = bytearray(b'abc')
data[GrowingStart():] = data
assert data == bytearray(b'aabcd')
assert calls == ['start', 'start']

def growing_source():
    data.append(100)
    yield 88
data = bytearray(b'abc')
data[1:] = growing_source()
assert data == bytearray(b'aX')

def shrinking_source():
    del data[:]
    yield 88
data = bytearray(b'abcdef')
data[4:6] = shrinking_source()
assert data == bytearray(b'X')

class ChangingStep:
    def __init__(self):
        self.count = 0
    def __index__(self):
        self.count += 1
        return 2 if self.count == 1 else 1
data = bytearray(b'abc')
step = ChangingStep()
data[::step] = b'XY'
assert data == bytearray(b'XY') and step.count == 2

class FailingStep:
    def __init__(self):
        self.count = 0
    def __index__(self):
        self.count += 1
        if self.count == 2:
            raise RuntimeError('second unpack')
        return 1
data = bytearray(b'abc')
step = FailingStep()
try:
    data[::step] = b'XY'
except RuntimeError as error:
    assert str(error) == 'second unpack'
else:
    raise AssertionError('expected callback failure')
assert data == bytearray(b'abc') and step.count == 2

calls.clear()
def failing_source():
    calls.append('source')
    raise RuntimeError('source failure')
    yield 0
data = bytearray(b'abc')
try:
    data[::Index('step', 1)] = failing_source()
except RuntimeError:
    pass
else:
    raise AssertionError('expected source failure')
assert calls == ['step', 'source']
assert data == bytearray(b'abc')
print('bytearray slice reentry: PASS')
