# range stop is the mapped slice boundary, not the last item plus the step.
assert repr(range(1, 11)[::3]) == 'range(1, 11, 3)'
assert repr(range(1, 11)[8:2:3]) == 'range(9, 3, 3)'
assert repr(range(20, 0, -2)[1:8:3]) == 'range(18, 4, -6)'
assert repr(range(1, 11)[::-3]) == 'range(10, 0, -3)'

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        if calls.count(self.name) != 1:
            raise AssertionError('duplicate index callback')
        return self.value

for step in [1, 2, -1, -2, 2147483647, -2147483648]:
    calls.clear()
    r = range(1, 11)
    result = r[Index('start', 1):Index('stop', 8):Index('step', step)]
    assert calls == ['step', 'start', 'stop']
    assert list(result) == list(r)[1:8:step]

calls.clear()
try:
    range(1, 11)[Index('start', 1):Index('stop', 8):Index('step', 0)]
except ValueError as error:
    assert str(error) == 'slice step cannot be zero'
else:
    raise AssertionError('expected ValueError')
assert calls == ['step']
print('range slice indices: PASS')
