"""CPython 3.12 native-width integer hash and subclass slot dispatch."""
import sys
modulus = (1 << (61 if sys.maxsize > 2**32 else 31)) - 1
class Code(int):
    pass
values = (False, True, -2, -1, 0, 1, 2, 256, 257, 2**31, 2**32,
          modulus-1, modulus, modulus+1, sys.maxsize, sys.maxsize+1,
          10**80, -(10**80))
for value in values:
    print('integer', repr(value), hash(value), hash(Code(value)))
for value in (-5, -1, 0, 2, 256):
    print('cached-result', value, hash(value) is hash(value))

class Hashed(int):
    def __hash__(self):
        print('custom-hash')
        return result
    def __index__(self):
        raise AssertionError('index called')
    def __int__(self):
        raise AssertionError('int called')

def show(label, obj):
    try:
        print(label, hash(obj))
    except Exception as error:
        print(label, type(error).__name__, str(error))

class Returned(int):
    def __hash__(self):
        raise AssertionError('returned hash called')
    def __index__(self):
        raise AssertionError('returned index called')
for result in (False, True, -1, -2, 2, modulus+2, sys.maxsize,
               sys.maxsize+1, 8*modulus+2, -8*modulus-2, Returned(2),
               Returned(8*modulus+2), 2.0, '2', None):
    print('return', repr(result))
    show('custom', Hashed(999))

class Unhashable(int):
    __hash__ = None
class EqOnly(int):
    def __eq__(self, other):
        return True
class Inherited(EqOnly):
    pass
class Restored(EqOnly):
    __hash__ = int.__hash__
for cls in (Unhashable, EqOnly, Inherited, Restored):
    show(cls.__name__, cls(2))

class Raising(int):
    def __hash__(self):
        raise ValueError('body failed')
show('body-error', Raising(2))
class Descriptor:
    def __get__(self, instance, owner):
        print('bind', owner.__name__, instance is not None)
        return lambda: 7
class Described(int):
    __hash__ = Descriptor()
show('descriptor', Described(2))
class BrokenDescriptor:
    def __get__(self, instance, owner):
        raise ValueError('binding failed')
Described.__hash__ = BrokenDescriptor()
show('binding-error', Described(2))
class NoneDescriptor:
    def __get__(self, instance, owner):
        return None
Described.__hash__ = NoneDescriptor()
show('binding-none', Described(2))

class StaticHash(int):
    __hash__ = staticmethod(lambda: 9)
class ClassHash(int):
    @classmethod
    def __hash__(cls):
        print('class', cls.__name__)
        return 10
show('static', StaticHash(2))
show('class', ClassHash(2))
class Mixin:
    def __hash__(self):
        return 11
class Mixed(Mixin, int):
    pass
class IntFirst(int, Mixin):
    pass
show('mixin', Mixed(2))
show('int-first', IntFirst(2))
x = Code(2)
x.__hash__ = lambda: 999
show('instance-ignored', x)
Code.__eq__ = lambda self, other: True
show('late-eq', x)
Code.__hash__ = lambda self: 17
show('late-hash', x)
Code.__hash__ = None
show('late-none', x)
del Code.__hash__
show('deleted-hash', x)

for args, kwargs in (((), {}), ((1, 2), {}), ((1,), {'bad': 1}), ((), {'bad': 1})):
    try:
        hash(*args, **kwargs)
    except TypeError as error:
        print('arity', str(error))
