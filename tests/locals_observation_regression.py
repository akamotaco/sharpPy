"""Python 3.12 optimized locals synchronization, without tracing hooks."""
import sys


def updated(x):
    x = 2
    y = 3
    assert locals()['x'] == 2
    assert locals()['y'] == 3
    assert sys._getframe().f_locals['x'] == 2
    assert sys._getframe().f_locals['y'] == 3
updated(1)


def shared(x):
    d = locals()
    x = 2
    assert d['x'] == 1
    e = locals()
    assert d is e
    assert d['x'] == 2
    assert sys._getframe().f_locals is d
    d['x'] = 99
    d['extra'] = 7
    assert x == 2
    assert locals()['x'] == 2
    assert locals()['extra'] == 7
    del x
    assert 'x' not in locals()
shared(1)


def outer(x):
    def inner():
        assert x == 7
        assert locals()['x'] == 7
        assert sys._getframe().f_locals['x'] == 7
    inner()
    x = 8
    assert locals()['x'] == 8
outer(7)


def deleted_cell(x):
    def inner():
        return x
    d = locals()
    assert d['x'] == 1
    del x
    assert 'x' not in locals()
deleted_cell(1)


def independent(x):
    return locals()
first = independent(1)
second = independent(2)
assert first is not second
assert first['x'] == 1 and second['x'] == 2


def retained(x):
    x = 4
    return sys._getframe()
assert retained(1).f_locals['x'] == 4


def suspended(x):
    d = locals()
    yield d
    x += 1
    yield locals()
g = suspended(10)
d = next(g)
assert d['x'] == 10
e = next(g)
assert d is e
assert d['x'] == 11


def arguments(a, /, b=2, *args, c=3, **kwargs):
    d = locals()
    assert d['a'] == 1 and d['b'] == 4
    assert d['args'] == (5,)
    assert d['c'] == 6 and d['kwargs']['other'] == 7
arguments(1, 4, 5, c=6, other=7)
print('locals observation regression: PASS')
