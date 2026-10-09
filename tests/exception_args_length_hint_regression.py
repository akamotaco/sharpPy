calls = []
class Source:
    def __init__(self, length, hint):
        self.length = length
        self.hint = hint
    def __iter__(self):
        calls.append('iter')
        return iter([1, 2])
    def __len__(self):
        calls.append('len')
        if self.length == 'type':
            raise TypeError('no length')
        if self.length == 'runtime':
            raise RuntimeError('length failed')
        return self.length
    def __length_hint__(self):
        calls.append('hint')
        if self.hint == 'type':
            raise TypeError('no hint')
        if self.hint == 'runtime':
            raise RuntimeError('hint failed')
        return self.hint

for length, hint, expected_calls in [(2, 'runtime', ['iter', 'len']),
                                    ('type', 0, ['iter', 'len', 'hint']),
                                    ('type', 8, ['iter', 'len', 'hint']),
                                    (1.5, True, ['iter', 'len', 'hint']),
                                    ('type', NotImplemented, ['iter', 'len', 'hint']),
                                    ('type', 'type', ['iter', 'len', 'hint'])]:
    calls.clear()
    error = StopIteration('old')
    error.args = Source(length, hint)
    assert error.args == (1, 2)
    assert error.value == 'old'
    assert calls == expected_calls

huge = 10 ** 100
for length, hint, error_type, message in [
    ('runtime', 2, RuntimeError, 'length failed'),
    (-1, 2, ValueError, '__len__() should return >= 0'),
    (-huge, 2, ValueError, '__len__() should return >= 0'),
    (huge, 2, OverflowError, "cannot fit 'int' into an index-sized integer"),
    ('type', 'runtime', RuntimeError, 'hint failed'),
    ('type', -1, ValueError, '__length_hint__() should return >= 0'),
    ('type', huge, OverflowError, 'Python int too large to convert to C ssize_t'),
    ('type', -huge, OverflowError, 'Python int too large to convert to C ssize_t'),
    ('type', 1.5, TypeError, '__length_hint__ must be an integer, not float')]:
    error = StopIteration('old')
    saved = error.args
    try:
        error.args = Source(length, hint)
    except error_type as failure:
        assert str(failure) == message
    else:
        raise AssertionError('expected hint failure')
    assert error.args is saved

class Index:
    def __index__(self):
        return 2
error.args = Source(Index(), 0)
assert error.args == (1, 2)
try:
    error.args = Source('type', Index())
except TypeError as failure:
    assert str(failure) == '__length_hint__ must be an integer, not Index'
else:
    raise AssertionError('hint must return int, not an indexable object')
class HintDescriptor:
    def __get__(self, instance, owner):
        raise TypeError('hint binding failed')
class BoundSource:
    __length_hint__ = HintDescriptor()
    def __iter__(self):
        return iter([])
saved = error.args
try:
    error.args = BoundSource()
except TypeError as failure:
    assert str(failure) == 'hint binding failed'
else:
    raise AssertionError('descriptor binding error must propagate')
assert error.args is saved

class FailedIterator:
    def __iter__(self):
        raise RuntimeError('iterator failed')
    def __len__(self):
        raise AssertionError('length called before iterator')
try:
    error.args = FailedIterator()
except RuntimeError as failure:
    assert str(failure) == 'iterator failed'
else:
    raise AssertionError('expected iterator failure')
assert error.args is saved
print('exception args length hint: PASS')
