import _operator
length_hint = _operator.length_hint
calls = []
class Default:
    def __index__(self):
        calls.append('default')
        return 7
class Source:
    def __len__(self):
        calls.append('len')
        raise TypeError('no length')
    def __length_hint__(self):
        calls.append('hint')
        return 3
assert length_hint(Source(), Default()) == 3
assert calls == ['default', 'len', 'hint']
assert length_hint(object(), -1) == -1
assert length_hint(object(), True) == 1
assert type(length_hint(object(), True)) is int
assert length_hint([1, 2], -1) == 2
assert length_hint('abc') == 3

class Sized:
    def __len__(self):
        return 4
    def __length_hint__(self):
        raise AssertionError('hint called after successful len')
assert length_hint(Sized()) == 4
class Unsupported:
    def __length_hint__(self):
        return NotImplemented
assert length_hint(Unsupported(), 9) == 9
class TypeFailure:
    def __length_hint__(self):
        raise TypeError('no hint')
assert length_hint(TypeFailure(), 8) == 8
class Failure:
    def __len__(self):
        raise RuntimeError('length failed')
try:
    length_hint(Failure(), 8)
except RuntimeError as error:
    assert str(error) == 'length failed'
else:
    raise AssertionError('expected RuntimeError')

for default, kind, message in [(1.5, TypeError, "'float' object cannot be interpreted as an integer"),
                                (10 ** 100, OverflowError, 'Python int too large to convert to C ssize_t'),
                                (-(10 ** 100), OverflowError, 'Python int too large to convert to C ssize_t')]:
    calls.clear()
    try:
        length_hint(Source(), default)
    except kind as error:
        assert str(error) == message
    else:
        raise AssertionError('expected default conversion failure')
    assert calls == []

for args, message in [((), 'length_hint expected at least 1 argument, got 0'),
                       ((1, 2, 3), 'length_hint expected at most 2 arguments, got 3')]:
    try:
        length_hint(*args)
    except TypeError as error:
        assert str(error) == message
    else:
        raise AssertionError('expected argument count failure')
try:
    length_hint([], default=1)
except TypeError as error:
    assert str(error) == '_operator.length_hint() takes no keyword arguments'
else:
    raise AssertionError('expected keyword failure')
print('operator length hint: PASS')
