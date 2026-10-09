"""dict.copy uses storage unless a subclass replaces the iteration slot."""
class Storage(dict):
    def __getitem__(self, key):
        raise AssertionError('unexpected item lookup')
    def keys(self):
        raise AssertionError('unexpected keys lookup')
    def __len__(self):
        raise AssertionError('unexpected length lookup')

class Child(Storage):
    pass

for cls in (Storage, Child):
    source = cls()
    value = [7]
    dict.__setitem__(source, 'first', 1)
    dict.__setitem__(source, 'value', value)
    dict.__delitem__(source, 'first')
    dict.__setitem__(source, 'first', 3)
    for copied in (source.copy(), dict.copy(source)):
        assert type(copied) is dict
        assert list(copied) == ['value', 'first']
        assert copied['value'] is value
        copied['own'] = 4
        assert 'own' not in source

events = []
class Mapping(dict):
    def __iter__(self):
        raise AssertionError('iteration slot is checked but not called')
    def keys(self):
        events.append('keys')
        return ['virtual', 'real']
    def __getitem__(self, key):
        events.append(key)
        return 42

source = Mapping()
# Empty underlying storage bypasses even the mapping callbacks.
assert source.copy() == {}
assert events == []
dict.__setitem__(source, 'real', 1)
assert dict.copy(source) == {'virtual': 42, 'real': 42}
assert events == ['keys', 'virtual', 'real']

events.clear()
class GeneratedKeys(Mapping):
    def keys(self):
        events.append('start')
        yield 'a'
        events.append('end')
        yield 'b'

source = GeneratedKeys()
dict.__setitem__(source, 'real', 1)
assert source.copy() == {'a': 42, 'b': 42}
assert events == ['start', 'end', 'a', 'b']

class LiveKeys(Mapping):
    def keys(self):
        self.names = ['a']
        return self.names
    def __getitem__(self, key):
        if key == 'a':
            self.names.append('b')
        return key

source = LiveKeys()
dict.__setitem__(source, 'real', 1)
assert source.copy() == {'a': 'a', 'b': 'b'}

class NonIterableKeys(Mapping):
    def keys(self):
        return 1

source = NonIterableKeys()
dict.__setitem__(source, 'real', 1)
try:
    source.copy()
except TypeError as error:
    assert str(error) == 'NonIterableKeys.keys() returned a non-iterable (type int)'
else:
    raise AssertionError('non-iterable keys accepted')

# Only type-level __iter__ matters, and later type changes must be observed.
class Changing(dict):
    def keys(self):
        return ['virtual']
    def __getitem__(self, key):
        return 9

source = Changing()
dict.__setitem__(source, 'real', 1)
source.__iter__ = None
assert source.copy() == {'real': 1}
Changing.__iter__ = None
assert source.copy() == {'virtual': 9}
del Changing.__iter__
assert source.copy() == {'real': 1}

class FailingItem(Mapping):
    def __getitem__(self, key):
        raise StopIteration('item error')

source = FailingItem()
dict.__setitem__(source, 'real', 1)
try:
    source.copy()
except StopIteration:
    pass
else:
    raise AssertionError('item StopIteration must propagate')

class FailingKeys(Mapping):
    def keys(self):
        yield 'a'
        raise ValueError('keys error')

source = FailingKeys()
dict.__setitem__(source, 'real', 1)
try:
    source.copy()
except ValueError:
    pass
else:
    raise AssertionError('keys iteration error must propagate')

for invalid in ([], 1, object()):
    try:
        dict.copy(invalid)
    except TypeError:
        pass
    else:
        raise AssertionError('non-dict accepted')
print('dict copy subclass: PASS')
