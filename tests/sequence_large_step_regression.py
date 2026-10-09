huge = 10 ** 100
steps = [2 ** 31, -(2 ** 31) - 1, 2 ** 63, -(2 ** 63), huge, -huge]
bounds = [None, -huge, -3, 0, 3, huge]
cases = 0
for step in steps:
    for start in bounds:
        for stop in bounds:
            key = slice(start, stop, step)
            equivalent = slice(start, stop, 999 if step > 0 else -999)
            for value in [[], [0, 1, 2, 3, 4, 5], (), (0, 1, 2, 3, 4, 5),
                          '', 'abcdef', b'', b'abcdef', bytearray(), bytearray(b'abcdef')]:
                assert value[key] == value[equivalent]
            for factory in [list, bytearray]:
                values = factory([0, 1, 2, 3, 4, 5])
                expected = factory(values)
                count = len(expected[equivalent])
                payload = b'\x09' * count if factory is bytearray else [9] * count
                expected[equivalent] = payload
                values[key] = payload
                assert values == expected
                del values[key]
                del expected[equivalent]
                assert values == expected
            data = bytearray(b'abcdef')
            expected = bytearray(data)
            del expected[equivalent]
            data[key] = b''
            assert data == expected
            cases += 1

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value

for value in [[0, 1, 2], (0, 1, 2), 'abc', b'abc', bytearray(b'abc')]:
    calls.clear()
    key = slice(Index('start', 0), Index('stop', 3), Index('step', huge))
    assert value[key] == value[:1]
    assert calls == ['step', 'start', 'stop']
    assert key.step.value == huge

data = [0, 1, 2]
try:
    data[::huge] = [8, 9]
except ValueError as error:
    assert str(error) == 'attempt to assign sequence of size 2 to extended slice of size 1'
else:
    raise AssertionError('expected ValueError')
assert data == [0, 1, 2]
print('sequence large step:', cases, 'PASS')
