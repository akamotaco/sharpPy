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
