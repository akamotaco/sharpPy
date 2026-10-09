for args in [(), ('message',), (None,), (42,), ((1, 2),), ('first', 2)]:
    error = StopIteration(*args)
    assert error.args == args
    assert error.value == (args[0] if args else None)
    assert str(error) == ('' if not args else str(args[0]) if len(args) == 1 else str(args))
    expected_repr = 'StopIteration(' + ', '.join(repr(item) for item in args) + ')'
    assert repr(error) == expected_repr
    error.value = 'changed'
    assert error.value == 'changed'
    assert error.args == args

calls = []
class Text:
    def __str__(self):
        calls.append('str')
        return 'display'
    def __repr__(self):
        calls.append('repr')
        return 'Text()'
value = Text()
error = StopIteration(value)
assert calls == []
assert error.value is value and error.args[0] is value
assert str(error) == 'display'
assert calls == ['str']
assert repr(error) == 'StopIteration(Text())'
assert calls == ['str', 'repr']

try:
    StopIteration(value=42)
except TypeError as error:
    assert str(error) == 'StopIteration() takes no keyword arguments'
else:
    raise AssertionError('expected TypeError')

for source in [iter([]), iter(()), iter('')]:
    try:
        next(source)
    except StopIteration as error:
        assert error.args == () and error.value is None
        assert str(error) == '' and repr(error) == 'StopIteration()'
    else:
        raise AssertionError('expected exhaustion')

def returning(value):
    yield 1
    return value
for value in [None, 42, (1, 2)]:
    source = returning(value)
    assert next(source) == 1
    try:
        next(source)
    except StopIteration as error:
        assert error.value == value
        assert error.args == (() if value is None else (value,))
    else:
        raise AssertionError('expected generator return')
print('StopIteration arguments: PASS')
