# Regression for exception formatting, independent of bytearray conversion.
assert str(StopIteration('value conversion')) == 'value conversion'
print('StopIteration message: PASS')
