"""Guard in-place dispatch, immutable fallback and reflected operations."""


def immutable():
    value = 5
    value += 2
    assert value == 7
    value -= 3
    assert value == 4
    value *= 3
    assert value == 12
    value //= 5
    assert value == 2
    value %= 2
    assert value == 0
    value += 3
    value **= 3
    assert value == 27
    value <<= 2
    assert value == 108
    value >>= 1
    assert value == 54
    value &= 15
    assert value == 6
    value |= 8
    assert value == 14
    value ^= 3
    assert value == 13
    value /= 2
    assert value == 6.5
    value = -7
    value //= 3
    assert value == -3
    value %= 2
    assert value == 1
    try:
        value //= 0
    except ZeroDivisionError:
        pass
    else:
        raise AssertionError("in-place division must raise ZeroDivisionError")
    huge = 2 ** 80
    huge += 1
    assert huge == 1208925819614629174706177
    text = "a"
    text += "b"
    text *= 2
    assert text == "abab"
    sequence = (1,)
    sequence += (2,)
    sequence *= 2
    assert sequence == (1, 2, 1, 2)


def mutable():
    values = [1]
    alias = values
    values += [2, 3]
    assert values is alias and values == [1, 2, 3]
    values *= 2
    assert values is alias and values == [1, 2, 3, 1, 2, 3]


def custom():
    class Counter:
        def __init__(self, value):
            self.value = value
        def __iadd__(self, other):
            self.value += other
            return self
    value = Counter(1)
    alias = value
    value += 4
    assert value is alias and value.value == 5

    class Fallback:
        def __iadd__(self, other):
            return NotImplemented
        def __add__(self, other):
            return other + 10
    value = Fallback()
    value += 2
    assert value == 12

    class Reflected:
        def __radd__(self, other):
            return other + 10
    value = 2
    value += Reflected()
    assert value == 12


immutable()
mutable()
custom()
print("binary in-place regression: PASS")
