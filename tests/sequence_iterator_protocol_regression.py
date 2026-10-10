calls = []
class Sequence:
    def __getitem__(self, index):
        calls.append(index)
        if index == 2:
            raise IndexError('end')
        return index + 10
seq = Sequence()
seq.__getitem__ = lambda index: 999
it = iter(seq)
assert next(it) == 10
assert list(it) == [11]
assert next(it, 'done') == 'done'
assert calls == [0, 1, 2]

failure = ValueError('retry')
class Retry:
    def __getitem__(self, index):
        calls.append(index)
        if len(calls) == 1:
            raise failure
        if index == 1:
            raise StopIteration('end')
        return 42
calls = []
it = iter(Retry())
try:
    next(it)
except ValueError as exc:
    assert exc is failure
else:
    assert False
assert next(it) == 42
assert next(it, 'done') == 'done'
assert next(it, 'done') == 'done'
assert calls == [0, 0, 1]

class GetItemDescriptor:
    def __get__(self, instance, owner):
        calls.append('bind')
        return lambda index: index + 3
class Described:
    __getitem__ = GetItemDescriptor()
calls = []
it = iter(Described())
assert calls == []
assert next(it) == 3
assert calls == ['bind']

class Missing:
    __getitem__ = None
it = iter(Missing())
try:
    next(it)
except TypeError as exc:
    assert str(exc) == "'NoneType' object is not callable"
else:
    assert False
print('sequence iterator regression passed')
