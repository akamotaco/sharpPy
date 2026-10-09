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
        for data in [b'abcdef', bytearray(b'abcdef'), 'abcdef',
                     [0, 1, 2, 3, 4, 5], (0, 1, 2, 3, 4, 5)]:
            result = data[start:end:step]
            assert list(result) == [data[i] for i in positions]

# Empty and small inputs, plus single callback evaluation in each read path.
for data in [b'', bytearray(), '', [], ()]:
    assert len(data[::2147483647]) == 0
    assert len(data[::-2147483648]) == 0

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value
for data in [b'abcdef', bytearray(b'abcdef'), 'abcdef',
             [0, 1, 2, 3, 4, 5], (0, 1, 2, 3, 4, 5)]:
    calls.clear()
    result = data[Index('start', 1):Index('stop', 6):Index('step', 2147483647)]
    assert list(result) == [data[1]]
    assert calls == ['step', 'start', 'stop']
print('slice read steps: PASS')
