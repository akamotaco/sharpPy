"""Exact int/float comparisons at precision and range boundaries."""

def check(integer, real, order):
    # order describes integer relative to real; None means unordered (NaN).
    expected = (
        order == 0,
        order != 0,
        order == -1,
        order == -1 or order == 0,
        order == 1,
        order == 1 or order == 0,
    )
    actual = (integer == real, integer != real, integer < real,
              integer <= real, integer > real, integer >= real)
    assert actual == expected, ('int-left', integer, real, actual, expected)
    reverse = (real == integer, real != integer, real > integer,
               real >= integer, real < integer, real <= integer)
    assert reverse == expected, ('float-left', integer, real, reverse, expected)


count = 0
for exponent in (0, 1, 30, 31, 32, 48, 52, 53, 54, 62, 63, 64,
                 100, 200, 500, 1000, 1023):
    base = 2 ** exponent
    real = float(base)
    for offset in (-2, -1, 0, 1, 2):
        integer = base + offset
        order = -1 if offset < 0 else 1 if offset > 0 else 0
        check(integer, real, order)
        check(-integer, -real, -order)
        count += 2

# Exercise non-power-of-two significands and their adjacent integer values.
for shift in (0, 1, 10, 11, 12, 47, 100, 500, 970):
    for mantissa in (3, 2 ** 52 - 1, 2 ** 52 + 1, 2 ** 53 - 1):
        base = mantissa * 2 ** shift
        real = float(base)
        for offset in (-3, -1, 0, 1, 3):
            order = -1 if offset < 0 else 1 if offset > 0 else 0
            check(base + offset, real, order)
            check(-(base + offset), -real, -order)
            count += 2

for integer, real, order in (
    (0, 0.0, 0), (0, -0.0, 0),
    (0, 5e-324, -1), (0, -5e-324, 1),
    (1, 0.5, 1), (-1, -0.5, -1),
    (1, 1.5, -1), (-1, -1.5, 1),
    (2 ** 51, 2251799813685248.5, -1),
    (-(2 ** 51), -2251799813685248.5, 1),
    (2 ** 2000, 1.0, 1), (-(2 ** 2000), -1.0, -1),
    (2 ** 2000, -1.0, 1), (-(2 ** 2000), 1.0, -1),
):
    check(integer, real, order)
    count += 1

for integer in (0, 1, -1, 2 ** 53 + 1, -(2 ** 53 + 1),
                2 ** 1024, -(2 ** 1024), 2 ** 2000, -(2 ** 2000)):
    check(integer, float('inf'), -1)
    check(integer, float('-inf'), 1)
    check(integer, float('nan'), None)
    count += 3

maximum = float('1.7976931348623157e308')
maximum_integer = (2 ** 53 - 1) * 2 ** 971
check(maximum_integer, maximum, 0)
check(maximum_integer - 1, maximum, -1)
check(maximum_integer + 1, maximum, 1)
check(-maximum_integer, -maximum, 0)
check(-maximum_integer - 1, -maximum, -1)
check(-maximum_integer + 1, -maximum, 1)
count += 6

# Comparisons used by containers must see the same exact numeric values.
assert (2 ** 53 + 1) not in [float(2 ** 53)]
assert float(2 ** 53) not in [2 ** 53 + 1]
assert [2 ** 53 + 1] != [float(2 ** 53)]
assert [2 ** 53 + 1] > [float(2 ** 53)]
print('mixed numeric comparisons:', count)
