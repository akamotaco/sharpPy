"""CPython 3.12 close state transitions and observable cleanup errors."""
events = []
def fresh():
    events.append('started')
    yield 1
g = fresh()
assert g.close() is None
assert g.close() is None
assert events == []
assert next(g, 'done') == 'done'

failure = ValueError('cleanup')
def failing():
    try:
        yield 1
    finally:
        raise failure
g = failing()
assert next(g) == 1
try:
    g.close()
except ValueError as caught:
    assert caught is failure
    assert type(caught.__context__) is GeneratorExit
    assert caught.__context__.args == ()
else:
    assert False
assert next(g, 'done') == 'done'
assert g.close() is None

def refusing():
    try:
        yield 1
    except GeneratorExit as error:
        assert error.args == ()
        yield 2
        yield 3
g = refusing()
assert next(g) == 1
try:
    g.close()
except RuntimeError as error:
    assert str(error) == 'generator ignored GeneratorExit'
else:
    assert False
assert next(g) == 3
assert g.close() is None
assert next(g, 'done') == 'done'

def returns():
    try:
        yield 1
    except GeneratorExit:
        return 42
g = returns()
assert next(g) == 1
assert g.close() is None

def reentrant():
    try:
        g.throw(123)
    except TypeError:
        pass
    else:
        assert False, 'invalid throw argument accepted'
    try:
        g.close()
    except ValueError as error:
        assert str(error) == 'generator already executing'
        events.append('reentry rejected')
    yield 1
g = reentrant()
assert next(g) == 1
assert events == ['reentry rejected']
assert g.close() is None
print('generator close state: PASS')
