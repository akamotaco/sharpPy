# P0-43: type flags decide "what kind of object is this", and a base that is not a type
# stops leaking a CLR cast failure. Every expectation below was measured against
# CPython 3.12.15 first (see docs/reports/2026-10-30/type_flags_and_unacceptable_bases.md).

def show(fn):
    try:
        r = fn()
        return 'ok ' + type(r).__name__
    except Exception as e:
        return type(e).__name__ + ': ' + str(e)


# --- a base without Py_TPFLAGS_BASETYPE is refused before any MRO work -------------------
# CPython: Objects/typeobject.c:2789, and the bit really is absent from the specs
# (Objects/boolobject.c:190, Objects/object.c:1894, Objects/sliceobject.c:696).
print(show(lambda: type('C', (bool,), {})))
print(show(lambda: type('C', (type(None),), {})))
print(show(lambda: type('C', (slice,), {})))
print(show(lambda: type('C', (int,), {})))
print(show(lambda: type('C', (str,), {})))
print(show(lambda: type('C', (object,), {})))

# bool is the case where absence of the bit is what says so: its base int is subclassable.
print(show(lambda: type('D', (int,), {})))


# --- a base that is not a type drags its own type in as the metaclass -------------------
# CPython: the winner must be a (non-strict) subclass of type (Objects/typeobject.c:3083),
# so all four of these raise the conflict message rather than creating a class.
class Inst:
    pass


for label, base in (('int', 5), ('instance', Inst()), ('function', len), ('none', None)):
    print(label, show(lambda b=base: type('C', (b,), {})))



# --- the family bits are what make an inherited slot apply to a deep subclass -----------
# inherit_special (Objects/typeobject.c:6726) propagates the bit to every subtype, so
# "list-ness" is a property of the type, discovered once, not of the instance.


class A(list):
    pass


class B(A):
    pass


class E(B):
    pass


b = E([1, 2, 3])
print(len(b), b[0], b[1:3], 2 in b, [1, 2, 3] == b, b == [1, 2, 3])
print(repr(b), str(b), bool(E()), list(b) == [1, 2, 3])
b.append(4)
b += [5]
print(b, type(b).__name__)


class DA(dict):
    pass


class DB(DA):
    pass


d = DB({'a': 1})
d['b'] = 2
print(len(d), d['a'], 'b' in d, sorted(d), repr(d), d == {'a': 1, 'b': 2})
d.update({'c': 3})
print(sorted(d.keys()), d.pop('c'), bool(DB()))


class SA(set):
    pass


class SB(SA):
    pass


s = SB([1, 2])
s.add(3)
s.discard(1)
print(sorted(s), len(s), 2 in s, repr(s), s == {2, 3})
print(sorted(s | {9}), sorted(s & {2}), sorted(s - {2}), sorted(s ^ {9}))


class FA(frozenset):
    pass


f = FA([1, 2])
print(sorted(f), hash(f) == hash(frozenset([1, 2])), repr(f), f <= {1, 2, 3})


# --- the metaclass gate must not disturb metaclasses ------------------------------------
class Meta(type):
    pass


class WithMeta(metaclass=Meta):
    pass


print(type(WithMeta).__name__, WithMeta.__mro__[1].__name__)
print(isinstance(WithMeta(), WithMeta))
