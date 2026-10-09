"""Known gap: keys/values/items must be retained live views, not lists."""
d = {'before': 1}
keys = d.keys()
values = d.values()
items = d.items()
d['after'] = 2
assert list(keys) == ['before', 'after']
assert list(values) == [1, 2]
assert list(items) == [('before', 1), ('after', 2)]
d['before'] = 3
del d['after']
assert list(keys) == ['before']
assert list(values) == [3]
assert list(items) == [('before', 3)]
print('dict retained views: PASS')
