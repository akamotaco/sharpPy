"""Same-size mutation is not equivalent to any structural-change guard."""
d = {'first': 1, 'second': 2}
iterator = iter(d.keys())
assert next(iterator) == 'first'
del d['second']
d['third'] = 3
assert next(iterator) == 'third'
assert next(iterator, 'done') == 'done'
print('dict iterator same size: PASS')
