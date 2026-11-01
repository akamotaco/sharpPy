import math

for a, b in [(2**120 + 12345, 7), (10**30, 10**29), (2**200, 3**100), (2**64 + 1, 3), (3, 2**64 + 1), (2**53 + 1, 1),
             (2**54 + 3, 2), (-(2**100), 3), (2**100, -3), (1, 3**700), (1, 2**1074), (3, 2**1075), (1, 2**1076),
             (10**308, 1), (17976931348623157 * 10**292, 1), (9007199254740993, 1), (5, 2**70)]:
    print(a / b)
try:
    print(10**400 / 3)
except OverflowError as e:
    print(e)
print(0 / -5, 0 / 2**100, 0 / -(2**100), 7 / 2, -7 / 2)
print(float(2**64 + 2**11 + 1), float(2**64 + 2**11), float(2**53 + 1), float(2**53 + 3), float(-(2**70) - 2**17 - 1), float(10**22))
print(2**64 + 2**11 + 1 + 0.0, 1.5 + 2**70, 2**70 * 0.5, 2.0 ** 70 == 2**70, float(2**1023), int(1e22), int(2.5e30))
for bad in (2**1024, -(2**1024)):
    try:
        float(bad)
    except OverflowError as e:
        print(e)
print(math.isqrt(0), math.isqrt(1), math.isqrt(15), math.isqrt(16), math.isqrt(2**400 + 1), math.isqrt(True))
try:
    math.isqrt(-1)
except ValueError as e:
    print(e)
