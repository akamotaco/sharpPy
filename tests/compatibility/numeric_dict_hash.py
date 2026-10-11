"""Pending: equal integral float/int keys must share the dictionary hash."""
for exponent in (31, 32, 53, 63, 100, 500):
    integer = 2 ** exponent
    real = float(integer)
    assert integer == real
    print(exponent, {integer: 'int'}.get(real, 'missing'),
          {real: 'float'}.get(integer, 'missing'))
