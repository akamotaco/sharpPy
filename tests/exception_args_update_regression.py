for error in [StopIteration('old'), ValueError('old'), RuntimeError('old')]:
    original = error.args
    assert error.args is original
    source = ['new', 2]
    error.args = source
    source[0] = 'changed'
    assert error.args == ('new', 2)
    assert str(error) == "('new', 2)"
    values = (42,)
    error.args = values
    assert error.args is values
    assert str(error) == '42'
    error.args = []
    assert error.args == () and str(error) == ''
    error.args = 'ab'
    assert error.args == ('a', 'b')
    error.args = ('saved',)
    saved = error.args
    try:
        error.args = 123
    except TypeError as failure:
        assert str(failure) == "'int' object is not iterable"
    else:
        raise AssertionError('expected non-iterable error')
    assert error.args is saved
    try:
        del error.args
    except TypeError as failure:
        assert str(failure) == 'args may not be deleted'
    else:
        raise AssertionError('expected deletion error')
    assert error.args is saved

first = StopIteration()
second = StopIteration()
assert first.args is second.args
first.args = [1]
second.args = [1]
assert first.args is not second.args

error = StopIteration(7)
saved = error.args
calls = []
def successful():
    calls.append(error.args is saved)
    yield 1
    calls.append(error.args is saved)
    yield 2
error.args = successful()
assert error.args == (1, 2)
assert calls == [True, True]
assert error.value == 7

saved = error.args
def failing():
    yield 3
    raise RuntimeError('conversion failed')
try:
    error.args = failing()
except RuntimeError as failure:
    assert str(failure) == 'conversion failed'
else:
    raise AssertionError('expected source failure')
assert error.args is saved

def reentrant():
    error.args = ('inner',)
    yield 4
    raise RuntimeError('after inner assignment')
try:
    error.args = reentrant()
except RuntimeError:
    pass
else:
    raise AssertionError('expected reentrant failure')
assert error.args == ('inner',)
assert error.value == 7
print('exception args update: PASS')
