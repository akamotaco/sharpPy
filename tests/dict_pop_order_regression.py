"""Deletion must remove equal keys from lookup storage and insertion order."""
key = int('1000')
equal_key = int('1000')
d = {key: 'first', 2000: 'second'}
assert d.pop(equal_key) == 'first'
assert len(d) == 1
assert list(d) == [2000]
assert list(d.keys()) == [2000]
assert list(d.values()) == ['second']
assert list(d.items()) == [(2000, 'second')]
assert d.copy() == {2000: 'second'}
d[key] = 'third'
assert list(d) == [2000, 1000]
assert d.popitem() == (1000, 'third')
assert d.popitem() == (2000, 'second')
assert not d
assert list(d) == []
assert d.pop(equal_key, 'default') == 'default'
try:
    d.pop(equal_key)
except KeyError:
    pass
else:
    raise AssertionError('missing key must raise KeyError')

d = {int('1000'): 1, 2000: 2}
del d[int('1000')]
assert list(d.items()) == [(2000, 2)]
d = {1: 'integer', 2: 'remaining'}
assert d.pop(True) == 'integer'
assert list(d.items()) == [(2, 'remaining')]
print('dict pop order: PASS')
