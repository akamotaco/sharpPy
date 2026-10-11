"""Inherited numeric comparisons preserve the full int subclass payload."""
class Number(int):
    pass


class Child(Number):
    pass


class Other(int):
    pass


class NoConversions(int):
    def __int__(self):
        raise AssertionError('comparison called __int__')

    def __index__(self):
        raise AssertionError('comparison called __index__')

    def __float__(self):
        raise AssertionError('comparison called __float__')


def check(left, right, order):
    expected = (order == 0, order != 0, order == -1,
                order == -1 or order == 0, order == 1,
                order == 1 or order == 0)
    actual = (left == right, left != right, left < right,
              left <= right, left > right, left >= right)
    reverse = (right == left, right != left, right > left,
               right >= left, right < left, right <= left)
    assert actual == expected, ('forward', actual, expected)
    assert reverse == expected, ('reverse', reverse, expected)


count = 0
for cls in (Number, Child, Other, NoConversions):
    for value in (0, 1, -1, 2 ** 53 + 1, -(2 ** 53 + 1),
                  2 ** 63 - 1, 2 ** 63, -(2 ** 63), -(2 ** 63) - 1,
                  2 ** 100, -(2 ** 100), 2 ** 2000, -(2 ** 2000)):
        number = cls(value)
        check(number, value, 0)
        check(number, value - 1, 1)
        check(number, value + 1, -1)
        check(number, Other(value), 0)
        check(number, Other(value - 1), 1)
        check(number, Other(value + 1), -1)
        check(number, float('inf'), -1)
        check(number, float('-inf'), 1)
        check(number, float('nan'), None)
        count += 9
    for exponent in (0, 31, 53, 63, 100, 1023):
        base = 2 ** exponent
        real = float(base)
        for offset in (-1, 0, 1):
            check(cls(base + offset), real, offset)
            check(cls(-(base + offset)), -real, -offset)
            count += 2
    for value in (-1, 0, 1, 2, 2 ** 100, -(2 ** 100)):
        for boolean in (False, True):
            order = -1 if value < int(boolean) else 1 if value > int(boolean) else 0
            check(cls(value), boolean, order)
            count += 1
    check(cls(0), 5e-324, -1)
    check(cls(0), -5e-324, 1)
    check(cls(1), 1.5, -1)
    check(cls(-1), -1.5, 1)
    count += 4

assert Number(2 ** 100) in [2 ** 100]
assert 2 ** 100 in [Number(2 ** 100)]
assert [Number(2 ** 100)] == [float(2 ** 100)]
assert [float(2 ** 100)] == [Number(2 ** 100)]
assert [Number(2 ** 53 + 1)] > [float(2 ** 53)]

for value in (0, 1, 2 ** 100, -(2 ** 100)):
    key = Number(value)
    mapping = {value: 'base'}
    assert mapping[key] == 'base'
    mapping[key] = 'updated'
    assert len(mapping) == 1
    assert list(mapping)[0] is value
    assert mapping.pop(key) == 'updated'
    assert len(mapping) == 0
    assert {key: 'subclass'}[value] == 'subclass'

# A declined inherited ordering operation reaches the other operand intact.
original = Number(2 ** 100)

class Reflected:
    def __gt__(self, other):
        assert other is original
        return 'gt'

    def __ge__(self, other):
        assert other is original
        return 'ge'

    def __lt__(self, other):
        assert other is original
        return 'lt'

    def __le__(self, other):
        assert other is original
        return 'le'

right = Reflected()
assert (original < right) == 'gt'
assert (original <= right) == 'ge'
assert (original > right) == 'lt'
assert (original >= right) == 'le'
print('inherited int subclass comparisons:', count)
