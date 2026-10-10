"""Observe caller state and generator-local handlers with actual CPython."""
import sys

def observe(expected):
    assert sys.exception() is expected
    assert sys.exc_info()[1] is expected

def reraiser():
    raise

def explicit_cause():
    raise TypeError('explicit') from None

def nested(expected):
    observe(expected)
    try:
        raise TypeError('inner')
    except TypeError as inner:
        assert inner.__context__ is expected
        observe(inner)
    observe(expected)
    if expected is not None:
        try:
            reraiser()
        except BaseException as caught:
            assert caught is expected
        else:
            assert False, 'bare raise returned'
        try:
            explicit_cause()
        except TypeError as caught:
            assert caught.__context__ is expected
            assert caught.__cause__ is None
            assert caught.__suppress_context__ is True
        else:
            assert False, 'explicit raise returned'

observe(None)
outer = ValueError('outer')
try:
    raise outer
except ValueError:
    nested(outer)
    observe(outer)
observe(None)

def relay():
    yield sys.exception()
    yield sys.exception()
    yield sys.exception()

g = relay()
assert next(g) is None
try:
    raise outer
except ValueError:
    assert next(g) is outer
assert next(g) is None
g.close()

# A suspended generator's own handler must override its current caller.
local = TypeError('generator')
def holding():
    try:
        raise local
    except TypeError:
        yield sys.exception()
        nested(local)
        yield sys.exception()
    yield sys.exception()

g = holding()
assert next(g) is local
observe(None)
try:
    raise outer
except ValueError:
    assert next(g) is local
    observe(outer)
    assert next(g) is outer
observe(None)
g.close()

# A generator created under one handler must use the resumer, not its creator.
try:
    raise outer
except ValueError:
    g = relay()
assert next(g) is None
g.close()
print('caller and generator exception state: PASS')
