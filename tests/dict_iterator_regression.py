"""Dictionary iteration: live values, size guards and sticky exhaustion."""
for kind in ('keys', 'values', 'items'):
    d = {'first': 1, 'second': 2}
    view = getattr(d, kind)()
    iterator = iter(view)
    first = next(iterator)
    d['second'] = 3
    second = next(iterator)
    if kind == 'keys':
        assert (first, second) == ('first', 'second')
    elif kind == 'values':
        assert (first, second) == (1, 3)
    else:
        assert (first, second) == (('first', 1), ('second', 3))
    try:
        next(iterator)
    except StopIteration:
        pass
    else:
        raise AssertionError('iterator must exhaust')
    d['third'] = 4
    assert next(iterator, 'done') == 'done'

    d = {'first': 1, 'second': 2}
    iterator = iter(getattr(d, kind)())
    next(iterator)
    d['third'] = 3
    try:
        next(iterator)
    except RuntimeError as error:
        assert str(error) == 'dictionary changed size during iteration'
    else:
        raise AssertionError('growth must be detected')
    del d['third']
    try:
        next(iterator)
    except RuntimeError:
        pass
    else:
        raise AssertionError('size failure must stay sticky')

    d = {'first': 1, 'second': 2}
    iterator = iter(getattr(d, kind)())
    del d['second']
    try:
        next(iterator)
    except RuntimeError:
        pass
    else:
        raise AssertionError('shrink must be detected')

d = {'first': 1}
iterator = iter(d)
assert next(iterator) == 'first'
assert next(iterator, 'done') == 'done'
d['second'] = 2
assert next(iterator, 'done') == 'done'
print('dict iterator: PASS')
