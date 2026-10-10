def error(call, kind, message):
    try:
        call()
    except kind as exc:
        assert str(exc) == message, str(exc)
    else:
        assert False, message

class Iterator:
    def __iter__(self):
        return self
    def __next__(self):
        return 7

obj = Iterator()
obj.__iter__ = lambda: 42
assert iter(obj) is obj
assert next(iter(obj)) == 7

class Invalid:
    def __iter__(self):
        return 42
error(lambda: iter(Invalid()), TypeError, "iter() returned non-iterator of type 'int'")

class InstanceNext:
    def __iter__(self):
        return self
bad = InstanceNext()
bad.__next__ = lambda: 1
error(lambda: iter(bad), TypeError, "iter() returned non-iterator of type 'InstanceNext'")

class NoneNext:
    __next__ = None
    def __iter__(self):
        return self
none_next = NoneNext()
assert iter(none_next) is none_next
error(lambda: next(none_next), TypeError, "'NoneType' object is not callable")

calls = []
class NextDescriptor:
    def __get__(self, instance, owner):
        calls.append('next binding')
        return lambda: 11
class DescriptorIterator:
    __next__ = NextDescriptor()
    def __iter__(self):
        return self
di = DescriptorIterator()
assert iter(di) is di
assert calls == []
assert next(di) == 11
assert calls == ['next binding']

class IterDescriptor:
    def __get__(self, instance, owner):
        calls.append('iter binding')
        return lambda: obj
class Iterable:
    __iter__ = IterDescriptor()
assert iter(Iterable()) is obj
assert calls == ['next binding', 'iter binding']

class StaticIterable:
    __iter__ = staticmethod(lambda: obj)
assert iter(StaticIterable()) is obj

class Disabled:
    __iter__ = None
    def __getitem__(self, index):
        assert False, 'fallback must not run'
error(lambda: iter(Disabled()), TypeError, "'Disabled' object is not iterable")

class InstanceOnly:
    pass
only = InstanceOnly()
only.__iter__ = lambda: obj
only.__getitem__ = lambda index: index
error(lambda: iter(only), TypeError, "'InstanceOnly' object is not iterable")

failure = ValueError('iteration failure')
class Raising:
    def __iter__(self):
        raise failure
try:
    iter(Raising())
except ValueError as exc:
    assert exc is failure
else:
    assert False

class Mapping(dict):
    pass
assert list(Mapping(a=1)) == ['a']
class DisabledMapping(dict):
    __iter__ = None
error(lambda: iter(DisabledMapping()), TypeError, "'DisabledMapping' object is not iterable")

from _collections import deque
class DequeIterable:
    def __iter__(self):
        return iter(deque([3, 4]))
assert list(DequeIterable()) == [3, 4]

print('iter protocol regression passed')

