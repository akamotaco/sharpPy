# Validate mapped endpoints as well as selected values: range slices preserve
# their exact step, including when a slice is empty or has only one element.
huge = 10 ** 100
count = 0
checksum = 0
for start, stop, step in ((0, 12, 1), (12, -3, -2), (0, 0, 1),
                          (-(2 ** 63), 2 ** 63 - 1, 2 ** 63 - 1),
                          (huge, huge + 12, 1)):
    r = range(start, stop, step)
    values = list(r)
    for stride in (None, True, 1, -1, 2, -(2 ** 31), 2 ** 31 - 1,
                   2 ** 31, huge, -huge):
        for a in (None, -huge, -1, 0, 3, huge):
            for b in (None, -huge, -1, 0, 3, huge):
                key = slice(a, b, stride)
                first, last, jump = key.indices(len(r))
                result = r[key]
                assert (result.start, result.stop, result.step) == (
                    start + first * step, start + last * step, step * jump)
                assert list(result) == values[key]
                count += 1
                checksum += result.start * 3 + result.stop * 5 + result.step * 7

calls = []
class Index:
    def __init__(self, name, value, fail=False):
        self.name, self.value, self.fail = name, value, fail
    def __index__(self):
        calls.append(self.name)
        if self.fail:
            raise RuntimeError(self.name)
        return self.value

for stride in (False, 0, Index('step', 0)):
    calls.clear()
    try:
        range(10)[Index('start', 0):Index('stop', 3):stride]
        assert False
    except ValueError as error:
        assert str(error) == 'slice step cannot be zero'
    assert calls == (['step'] if isinstance(stride, Index) else [])

for stride in (None, True, -1, -(2 ** 31), 2 ** 31, Index('step', 1)):
    calls.clear()
    try:
        range(10)[Index('start', 0, True):Index('stop', 3):stride]
        assert False
    except RuntimeError as error:
        assert str(error) == 'start'
    assert calls == (['step', 'start'] if isinstance(stride, Index) else ['start'])
print('range slice step:', count, checksum)
