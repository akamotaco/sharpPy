try:
    raise ValueError('handled')
except ValueError:
    pass

try:
    raise TypeError('later')
except TypeError as error:
    assert error.__context__ is None
print('exception context restored: PASS')
