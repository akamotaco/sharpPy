# range preserves arbitrary precision step, unlike storage/view clipping.
huge = 10 ** 100
result = range(1, 7)[::huge]
assert list(result) == [1]
assert repr(result) == 'range(1, 7, ' + str(huge) + ')'
result = range(1, 7)[::-huge]
assert list(result) == [6]
assert repr(result) == 'range(6, 0, ' + str(-huge) + ')'
print('range large step: PASS')
