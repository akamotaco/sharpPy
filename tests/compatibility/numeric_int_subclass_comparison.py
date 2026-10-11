"""Pending: inherited int subclass comparisons must preserve the full payload."""
class Number(int):
    pass

number = Number(2 ** 100)
real = float(2 ** 100)
print('equality', number == real, real == number)
print('ordering', number <= real, real >= number)
