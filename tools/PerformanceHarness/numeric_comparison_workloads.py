def mixed_compare_small(n):
    limit = 100000.5
    total = 0
    for i in range(n):
        if i < limit and limit > i and i != limit and limit != i:
            total += i
    return total


def mixed_compare_large(n):
    integer = 2 ** 100
    real = float(integer)
    total = 0
    for i in range(n):
        if integer == real and real <= integer:
            total += i
    return total


def integer_compare(n):
    limit = 100001
    total = 0
    for i in range(n):
        if i < limit and limit > i and i != limit and limit != i:
            total += i
    return total


class _Number(int):
    pass


def int_subclass_compare(n):
    number = _Number(10)
    limit = 20
    total = 0
    for i in range(n):
        if number < limit and number <= limit and number != limit and number > 0:
            total += i
    return total


def int_subclass_float_compare(n):
    number = _Number(10)
    limit = 20.5
    total = 0
    for i in range(n):
        if number < limit and number <= limit and number != limit and number > 0.0:
            total += i
    return total


class _Comparable:
    def __lt__(self, other):
        return True


def user_compare(n):
    left = _Comparable()
    right = _Comparable()
    total = 0
    for i in range(n):
        if left < right:
            total += i
    return total
