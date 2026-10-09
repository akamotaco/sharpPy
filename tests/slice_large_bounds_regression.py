huge = 10 ** 100
boundaries = [-huge, -9223372036854775809, -2147483649, -2147483648, -6, -1,
              0, 1, 6, 2147483647, 2147483648, 9223372036854775808, huge]
steps = [1, 2, -1, -2]

def normalized(bound, step):
    if bound < 0:
        bound += 6
    if step > 0:
        return max(0, min(6, bound))
    return max(-1, min(5, bound))

for start in boundaries:
    for stop in boundaries:
        for step in steps:
            a = normalized(start, step)
            b = normalized(stop, step)
            for sequence in ([0, 1, 2, 3, 4, 5], (0, 1, 2, 3, 4, 5),
                             'abcdef', b'abcdef', bytearray(b'abcdef')):
                # -1 as a normalized reverse endpoint is the sentinel, not
                # an explicit Python -1 bound (which would mean the last item).
                expected = [sequence[i] for i in range(a, b, step)]
                actual = sequence[start:stop:step]
                assert list(actual) == expected
            view = memoryview(b'abcdef')
            child = view[start:stop:step]
            assert [child[i] for i in range(len(child))] == [b'abcdef'[i] for i in range(a, b, step)]
            child.release()
            view.release()

data = bytearray(b'abcdef')
view = memoryview(data)
view[-huge:huge] = b'123456'
assert data == bytearray(b'123456')
view[huge:-huge:-2] = b'XYZ'
assert data == bytearray(b'1Z3Y5X')
view.release()
data[-huge:huge] = b'ab'
assert data == bytearray(b'ab')
items = [0, 1, 2]
items[-huge:huge] = [3, 4]
assert items == [3, 4]

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value
actual = b'abc'[Index('start', -huge):Index('stop', huge):Index('step', 1)]
assert list(actual) == [97, 98, 99]
assert calls == ['step', 'start', 'stop']

class Broken:
    def __index__(self):
        raise RuntimeError('index failure')
try:
    b'abc'[Broken():huge]
except RuntimeError as error:
    assert str(error) == 'index failure'
else:
    raise AssertionError('expected index failure')
print('large slice bounds: PASS')
