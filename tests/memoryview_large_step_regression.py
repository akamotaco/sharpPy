import sys
maximum = sys.maxsize
huge = 10 ** 100
def signed(value):
    width = (maximum + 1) * 2
    return (value + maximum + 1) % width - maximum - 1

for step in [2 ** 31, -(2 ** 31) - 1, maximum, maximum + 1, -maximum - 1, huge, -huge]:
    clipped = max(-maximum, min(maximum, step))
    data = bytearray(b'abcdef')
    root = memoryview(data)
    child = root[::step]
    assert child.strides == (clipped,)
    assert len(child) == 1
    assert child[0] == (97 if step > 0 else 102)
    for inner_step in [-1, 2, huge, -huge]:
        inner = child[::inner_step]
        inner_clipped = max(-maximum, min(maximum, inner_step))
        assert inner.strides == (signed(clipped * inner_clipped),)
        assert len(inner) == 1 and inner[0] == child[0]
        inner[0] = 88
        assert child[0] == 88
        inner.release()
    child[:] = b'Y'
    assert child[0] == 89
    child[::huge] = b'Z'
    assert child[0] == 90
    root[::step] = b'Q'
    assert child[0] == 81
    root[1:2] = child
    assert data[1] == 81
    empty = child[1:1:huge]
    assert len(empty) == 0
    assert empty.strides == (signed(clipped * maximum),)
    empty.release()
    child.release()
    root.release()
    data.append(0)

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value
root = memoryview(b'abc')
child = root[Index('start', 0):Index('stop', 3):Index('step', huge)]
assert calls == ['step', 'start', 'stop']
assert child.strides == (maximum,)
child.release()
try:
    child.strides
except ValueError as error:
    assert str(error) == 'operation forbidden on released memoryview object'
else:
    raise AssertionError('expected released view error')
root.release()
print('memoryview large step: PASS')
