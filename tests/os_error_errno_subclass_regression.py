"""Compare OSError's int-subclass dictionary lookup with CPython 3.12."""
class Code(int):
    pass

for value in (True, False, -1, 0, 2, 2**61-1, 10**80, -(10**80), Code(2)):
    print('int-hash', repr(value), int.__hash__(value))
for args, kwargs in (((), {}), ((2, 9), {}), ((2,), {'bad': 1}),
                     ((2, 9), {'bad': 1}), (('x', 9), {'bad': 1})):
    try:
        int.__hash__(*args, **kwargs)
    except TypeError as error:
        print('int-hash-error', str(error))


def show(label, code, *tail):
    try:
        error = OSError(code, 'message', *tail)
        print(label, type(error).__name__, error.errno is code, error.args,
              error.filename, error.filename2)
    except Exception as error:
        print(label, type(error).__name__, str(error))


for value in (0, 1, 2, 3, 4, 10, 11, 13, 17, 20, 21, 32, 35, 36, 37,
              53, 54, 58, 60, 61, 103, 104, 108, 110, 111, 114, 115,
              10035, 10036, 10037, 10053, 10054, 10058, 10060, 10061,
              999, -1, 10**80):
    show('plain', Code(value))


class Hashed(int):
    def __hash__(self):
        print('hash')
        return hash_result
    def __eq__(self, other):
        print('eq', other, type(other).__name__)
        return eq_result
    def __int__(self):
        raise AssertionError('int called')
    def __index__(self):
        raise AssertionError('index called')


# Native-width hash results must not always be reduced modulo sys.hash_info.modulus.
for hash_result in (2, 3, 4, 10, 32, 10053, 10054, 10058, 10060, 10061,
                    999, -1, -2, True, False, 2.0, '2', None,
                    Code(2), 2**61 + 1, 4 * (2**61-1) + 2,
                    8 * (2**61-1) + 2, 10**80):
    for eq_result in (True, False, NotImplemented, 0, 1, [], [1]):
        print('case', repr(hash_result), repr(eq_result))
        show('custom', Hashed(999))

hash_result = 2
eq_result = NotImplemented
show('fallback-equal', Hashed(2))
show('fallback-different', Hashed(999))
eq_result = True
show('filename', Hashed(999), 'source', None, 'dest')

class EqOnly(int):
    def __eq__(self, other):
        raise AssertionError('eq before hash')
class Child(EqOnly):
    pass
class Unhashable(int):
    __hash__ = None
for cls in (EqOnly, Child, Unhashable):
    show('unhashable', cls(2))
    print('hash-disabled', cls.__hash__ is None)

class Restored(EqOnly):
    __hash__ = int.__hash__
    def __eq__(self, other):
        print('restored-eq', other)
        return True
show('restored', Restored(2))

class RaisingHash(int):
    def __hash__(self):
        print('raising-hash')
        raise ValueError('hash failed')
class RaisingEq(int):
    __hash__ = int.__hash__
    def __eq__(self, other):
        print('raising-eq', other)
        raise AttributeError('eq failed')
show('hash-error', RaisingHash(2))
show('eq-error', RaisingEq(2))
# No dictionary lookup outside 2..5 args, on direct subclass construction,
# or when kwargs fail; a normalized Windows winerror replaces the errno first.
x = RaisingHash(2)
print('one', OSError(x).args)
print('six', OSError(x, 2, 3, 4, 5, 6).args)
print('direct', type(FileNotFoundError(x, 'message')).__name__)
try:
    OSError(x, 'message', bad=1)
except TypeError as error:
    print('kwargs-first', str(error))
if hasattr(OSError(), 'winerror'):
    print('normalized-first', type(OSError(x, 'message', None, 2)).__name__)

class Dynamic(int):
    pass
x = Dynamic(2)
x.__hash__ = lambda: 999
x.__eq__ = lambda other: False
show('instance-ignored', x)
Dynamic.__eq__ = lambda self, other: False
show('late-eq-keeps-hash', x)
Dynamic.__hash__ = lambda self: 999
show('late-hash', x)
del Dynamic.__hash__
show('deleted-hash', x)
del Dynamic.__eq__
show('deleted-eq', x)

class Descriptor:
    def __get__(self, instance, owner):
        print('bind', owner.__name__, instance is not None)
        return lambda: 2
class DescriptorHash(int):
    __hash__ = Descriptor()
show('descriptor', DescriptorHash(2))
class StaticHash(int):
    __hash__ = staticmethod(lambda: 2)
show('staticmethod', StaticHash(2))
class ClassHash(int):
    @classmethod
    def __hash__(cls):
        print('class-hash', cls.__name__)
        return 2
show('classmethod', ClassHash(2))

class HashMixin:
    def __hash__(self):
        print('mixin-hash')
        return 999
class Mixed(HashMixin, int):
    pass
show('mro-mixin-first', Mixed(2))
class IntFirst(int, HashMixin):
    pass
show('mro-int-first', IntFirst(2))
class NoneMixin:
    __hash__ = None
class MixedNone(NoneMixin, int):
    pass
show('mro-none', MixedNone(2))
class ReturnCode(int):
    def __hash__(self):
        raise AssertionError('returned int hash called')
    def __index__(self):
        raise AssertionError('returned int index called')
hash_result = ReturnCode(8 * (2**61-1) + 2)
eq_result = True
show('hash-return-subclass-overflow', Hashed(999))
class BadDescriptor:
    def __get__(self, instance, owner):
        raise AttributeError('binding failed')
class BadHash(int):
    __hash__ = BadDescriptor()
class BadEq(int):
    __hash__ = int.__hash__
    __eq__ = BadDescriptor()
show('hash-binding-error', BadHash(2))
show('eq-binding-error', BadEq(2))
show('eq-binding-error-unequal', BadEq(999))
class ValueDescriptor:
    def __get__(self, instance, owner):
        raise ValueError('binding value failed')
BadHash.__hash__ = ValueDescriptor()
BadEq.__eq__ = ValueDescriptor()
show('hash-binding-value-error', BadHash(2))
show('eq-binding-value-error', BadEq(2))
class NoneDescriptor:
    def __get__(self, instance, owner):
        return None
BadHash.__hash__ = NoneDescriptor()
show('hash-binding-none', BadHash(2))

# Methods are resolved again after hash callbacks mutate the class.
class Mutating(int):
    def __hash__(self):
        Mutating.__eq__ = lambda self, other: True
        return 2
show('hash-mutates-eq', Mutating(999))

# CPython retains the dictionary key object, including Winsock ints > 256.
seen_keys = []
class KeyIdentity(int):
    def __hash__(self):
        return 10053 if hasattr(OSError(), 'winerror') else 2
    def __eq__(self, other):
        print('same-key', not seen_keys or seen_keys[-1] is other)
        seen_keys.append(other)
        return True
show('key-identity-first', KeyIdentity(999))
show('key-identity-again', KeyIdentity(999))

# __eq__ results are truth-tested, including type-only special method lookup.
class Truth:
    def __bool__(self):
        print('bool')
        return truth_result
class Length:
    def __len__(self):
        print('len')
        return truth_result
class Index:
    def __index__(self):
        print('index')
        return 1
hash_result = 2
for cls in (Truth, Length):
    for truth_result in (True, False, 0, 1, -1, 10**80, None, Code(1), Index()):
        print('truth-case', cls.__name__, type(truth_result).__name__)
        eq_result = cls()
        show('truth', Hashed(999))
class RaisingTruth:
    def __bool__(self):
        raise AttributeError('bool failed')
eq_result = RaisingTruth()
show('truth-error', Hashed(999))
class NoTruth:
    pass
eq_result = NoTruth()
eq_result.__bool__ = lambda: False
show('truth-instance-ignored', Hashed(999))
