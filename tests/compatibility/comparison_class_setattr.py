"""Pending: explicit type.__setattr__ binding, independently of comparison caching."""
class Number(int):
    pass

number = Number(1)
print('before', number == 1)
type.__setattr__(Number, '__eq__', lambda self, other: 'changed')
print('after', number == 1)
