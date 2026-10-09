"""Retained dict views observe the dictionary at each new observation."""
d = {'first': 1}
keys, values, items = d.keys(), d.values(), d.items()
assert type(keys).__name__ == 'dict_keys'
assert type(values).__name__ == 'dict_values'
assert type(items).__name__ == 'dict_items'
assert len(keys) == len(values) == len(items) == 1
assert bool(keys) and bool(values) and bool(items)
assert 'first' in keys
assert 1 in values
assert ('first', 1) in items
assert ('first', 2) not in items
assert 'first' not in items
assert ['first', 1] not in items
assert repr(keys) == "dict_keys(['first'])"
assert repr(values) == 'dict_values([1])'
assert repr(items) == "dict_items([('first', 1)])"
assert tuple(keys) == ('first',)
assert keys.__len__() == 1
assert keys.__contains__('first')
assert next(values.__iter__()) == 1
assert items.__repr__() == repr(items)
assert list(keys) == list(keys)
d['second'] = 2
assert list(keys) == ['first', 'second']
assert list(values) == [1, 2]
assert list(items) == [('first', 1), ('second', 2)]
assert len(keys) == 2
d['first'] = 3
assert 1 not in values
assert 3 in values
assert ('first', 3) in items
del d['second']
assert list(keys) == ['first']
d.clear()
assert not keys and not values and not items
assert len(keys) == 0
d['again'] = [4]
assert [4] in values
assert ('again', [4]) in items
for view in (keys, values, items):
    try:
        view[0]
    except TypeError:
        pass
    else:
        raise AssertionError('views must not support indexing')
assert list(dict.keys(d)) == ['again']
assert list(dict.values(d)) == [[4]]
assert list(dict.items(d)) == [('again', [4])]
class Derived(dict):
    pass
subclass = Derived()
subclass['one'] = 1
subkeys = subclass.keys()
subclass['two'] = 2
assert list(subkeys) == ['one', 'two']
assert list(dict.keys(subclass)) == ['one', 'two']
assert list(subclass.values()) == [1, 2]
assert list(subclass.items()) == [('one', 1), ('two', 2)]
del d
assert list(keys) == ['again']
print('dict views: PASS')
