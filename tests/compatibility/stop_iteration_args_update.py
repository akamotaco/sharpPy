error = StopIteration(1)
error.args = (2, 3)
assert error.args == (2, 3)
assert error.value == 1
assert str(error) == '(2, 3)'
assert repr(error) == 'StopIteration(2, 3)'
print('StopIteration args update: PASS')
