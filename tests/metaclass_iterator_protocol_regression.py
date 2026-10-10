def error(call, kind, message):
    try:
        call()
    except kind as exc:
        assert str(exc) == message, str(exc)
    else:
        assert False, message

class Meta(type):
    def __iter__(cls):
        return iter([2, 3])
class DerivedMeta(Meta):
    pass
class Subject(metaclass=DerivedMeta):
    __iter__ = lambda: 42
assert list(Subject) == [2, 3]

class DisabledMeta(Meta):
    __iter__ = None
    def __getitem__(cls, index):
        assert False, 'disabled fallback'
class Disabled(metaclass=DisabledMeta):
    pass
error(lambda: iter(Disabled), TypeError, "'DisabledMeta' object is not iterable")

class NonCallableMeta(Meta):
    __iter__ = 42
class NonCallable(metaclass=NonCallableMeta):
    pass
error(lambda: iter(NonCallable), TypeError, "'int' object is not callable")

calls = []
class Descriptor:
    def __get__(self, instance, owner):
        calls.append((instance, owner))
        return lambda: iter([9])
class DescribedMeta(type):
    __iter__ = Descriptor()
class Described(metaclass=DescribedMeta):
    pass
assert list(Described) == [9]
assert calls == [(Described, DescribedMeta)]

class StaticMeta(type):
    __iter__ = staticmethod(lambda: iter([7]))
class Static(metaclass=StaticMeta):
    pass
assert list(Static) == [7]

class ClassMeta(type):
    @classmethod
    def __iter__(meta):
        assert meta is ClassMeta
        return iter([8])
class ClassBound(metaclass=ClassMeta):
    pass
assert list(ClassBound) == [8]

class SequenceMeta(type):
    def __getitem__(cls, index):
        if index == 2:
            raise IndexError
        return index + 4
class Sequence(metaclass=SequenceMeta):
    pass
assert list(Sequence) == [4, 5]

class IteratorMeta(type):
    def __iter__(cls):
        return cls
    def __next__(cls):
        if cls.value == 2:
            raise StopIteration('done')
        cls.value += 1
        return cls.value
class ClassIterator(metaclass=IteratorMeta):
    value = 0
    __next__ = lambda: 999
assert iter(ClassIterator) is ClassIterator
assert next(ClassIterator) == 1
assert next(ClassIterator) == 2
assert next(ClassIterator, 'end') == 'end'
ClassIterator.value = 0
def delegate():
    result = yield from ClassIterator
    assert result == 'done'
assert list(delegate()) == [1, 2]

failure = ValueError('iter failure')
class RaisingMeta(type):
    def __iter__(cls):
        raise failure
class Raising(metaclass=RaisingMeta):
    pass
try:
    iter(Raising)
except ValueError as exc:
    assert exc is failure
else:
    assert False

class LookupFailure:
    def __get__(self, instance, owner):
        raise failure
class FallbackMeta(SequenceMeta):
    __iter__ = LookupFailure()
class Fallback(metaclass=FallbackMeta):
    pass
assert list(Fallback) == [4, 5]

class Plain:
    __iter__ = lambda: iter([1])
error(lambda: iter(Plain), TypeError, "'type' object is not iterable")
error(lambda: next(Plain), TypeError, "'type' object is not an iterator")
print('metaclass iterator protocol passed')
