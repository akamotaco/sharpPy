"""Handled exception state must be restored after nested handlers."""
outer = ValueError('outer')
inner = TypeError('inner')
try:
    raise outer
except ValueError:
    try:
        raise inner
    except TypeError as caught:
        assert caught is inner
        assert caught.__context__ is outer
    try:
        raise
    except ValueError as caught:
        assert caught is outer

try:
    raise
except RuntimeError as caught:
    assert str(caught) == 'No active exception to reraise'
    assert caught.__context__ is None

for i in range(3):
    try:
        raise ValueError(i)
    except ValueError as caught:
        assert caught.__context__ is None

# A pending exception remains handled during exceptional finally execution.
original = ValueError('finally')
try:
    try:
        raise original
    finally:
        try:
            raise TypeError('nested')
        except TypeError as caught:
            assert caught.__context__ is original
except ValueError as caught:
    assert caught is original

# GeneratorExit derives directly from BaseException.
events = []
def gen():
    try:
        yield 1
    except BaseException as original:
        try:
            raise ValueError('closing')
        except ValueError as caught:
            assert caught.__context__ is original
        events.append('closed')
g = gen()
assert next(g) == 1
g.close()
assert events == ['closed']
print('handled exception state: PASS')
