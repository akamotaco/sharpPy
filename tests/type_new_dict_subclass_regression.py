"""type.__new__ accepts dict subclasses and reads their underlying storage."""
class Namespace(dict):
    def items(self):
        raise AssertionError('items override must not be called')

    def __getitem__(self, key):
        raise AssertionError('getitem override must not be called')

namespace = Namespace()
dict.__setitem__(namespace, 'value', 7)
created = type.__new__(type, 'Created', (), namespace)
assert created.value == 7

class Mapping:
    def items(self):
        return []

try:
    type.__new__(type, 'Invalid', (), Mapping())
except TypeError:
    pass
else:
    raise AssertionError('arbitrary mapping accepted by type.__new__')
print('type new dict subclass regression: PASS')
