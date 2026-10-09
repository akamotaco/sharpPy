"""Stable cursor cases that do not trigger CPython entry compaction."""
for kind in ('keys', 'values', 'items'):
    d = {'a': 1, 'b': 2, 'c': 3}
    iterator = iter(getattr(d, kind)())
    next(iterator)
    del d['b']
    d['d'] = 4
    rest = list(iterator)
    if kind == 'keys':
        assert rest == ['c', 'd']
    elif kind == 'values':
        assert rest == [3, 4]
    else:
        assert rest == [('c', 3), ('d', 4)]
    assert list(d) == ['a', 'c', 'd']

    d = {'a': 1, 'b': 2, 'c': 3}
    iterator = iter(getattr(d, kind)())
    next(iterator)
    del d['a']
    d['d'] = 4
    next(iterator)
    next(iterator)
    try:
        next(iterator)
    except RuntimeError as error:
        assert str(error) == 'dictionary keys changed during iteration'
    else:
        raise AssertionError('unexpected extra entry must fail')
    assert next(iterator, 'done') == 'done'

    d = {'a': 1, 'b': 2}
    iterator = iter(getattr(d, kind)())
    next(iterator)
    d.clear()
    d.update({'c': 3, 'd': 4})
    actual = next(iterator)
    if kind == 'keys':
        assert actual == 'd'
    elif kind == 'values':
        assert actual == 4
    else:
        assert actual == ('d', 4)
    assert next(iterator, 'done') == 'done'

d = {'a': 1, 'b': 2, 'c': 3}
first = iter(d)
second = iter(d)
assert next(first) == 'a'
del d['b']
d.setdefault('d', 4)
assert list(first) == ['c', 'd']
assert list(second) == ['a', 'c', 'd']
assert list(iter(d)) == ['a', 'c', 'd']
d = {'a': 1, 'b': 2}
iterator = iter(d)
assert next(iterator) == 'a'
assert next(iterator) == 'b'
assert d.popitem() == ('b', 2)
d['c'] = 3
assert next(iterator, 'done') == 'done'
d = {0: 0}
iterator = iter(d)
for i in range(100):
    del d[i]
    d[i + 1] = i + 1
assert next(iterator) == 100
assert next(iterator, 'done') == 'done'
print('dict cursor mutation: PASS')
