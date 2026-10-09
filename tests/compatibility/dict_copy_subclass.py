"""dict.copy accepts subclasses with inherited iteration and bypasses item overrides."""
class Derived(dict):
    def __getitem__(self, key):
        raise AssertionError('copy must read underlying storage')

derived = Derived()
dict.__setitem__(derived, 'value', [7])
copied = dict.copy(derived)
assert type(copied) is dict
assert copied == {'value': [7]}
print('dict copy subclass: PASS')
