"""Known gap: an existing iterator must observe later value replacement."""
d = {'first': 1, 'second': 2}
values = iter(d.values())
items = iter(d.items())
assert next(values) == 1
assert next(items) == ('first', 1)
d['second'] = 3
assert next(values) == 3
assert next(items) == ('second', 3)
print('dict view iterator live values: PASS')
