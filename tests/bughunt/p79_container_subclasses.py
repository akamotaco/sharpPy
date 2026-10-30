class L(list):
    pass


class D(dict):
    pass


class S(set):
    pass


class F(frozenset):
    pass


# repr comes from the base's inherited tp_repr, and set_repr writes Py_TYPE(so)->tp_name, so a
# subclass prints C({1, 2}) / C() while list and dict subclasses print plain contents.
print(repr(L([1, 2])), str(D({'a': 1})))
print(repr(S([1])), repr(F([1, 2])))
print(repr(S()), repr(F()))
print({'k': L([1, 2]), 'f': F([1])})


class R(list):
    def __repr__(self):
        return 'custom'


print(repr(R([1])), [R([1])], f'{R([2])}')

print(len(S([1, 2])), len(F([1, 2])), sorted(S([2, 1])))

s = S()
s.add(1)
print(repr(s), 1 in s)
print(sorted(F([1, 2]).union([3])), sorted(F([1, 2]) & {2}), sorted(F([1, 2]) - {1}))
print(sorted(F([1]).intersection([1, 9])), F([1]).issubset([1, 2]), F([1]).isdisjoint([2]))
d = D()
d['k'] = 1
print(d, d.get('k'), list(d.keys()))
l = L()
l.append(1)
print(l, l[0], l[0:1], len(l))

print(bool(L()), bool(S()), bool(D()), bool(F()), bool(L([1])))
print(1 in L([1]), 'a' in D({'a': 1}), list(F([1, 2])) == [1, 2])

print([1, 2] == L([1, 2]), L([1, 2]) == [1, 2], {1} == S([1]), {'a': 1} == D({'a': 1}))
print(frozenset([1]) == F([1]), L([1]) == 'x', L([1]) != [1, 2])

print(L([1]) + [2], [2] + L([1]), L([1]) * 2, L([1]) * 2 == [1, 1])
print(S([1]) | F([2]), {1} | S([2]), S([1, 2]) - {2}, S([1]) ^ F([2]))
print(F([1]) | {2}, F([1, 2]) >= F([1]), S([1]) <= {1, 2})
print(D({'a': 1}) | {'b': 2})

print(hash(F([1, 2])) == hash(frozenset([1, 2])), {F([1, 2]): 'v'}[frozenset([1, 2])])
for factory, name in ((S, 'S'), (L, 'L'), (D, 'D')):
    try:
        hash(factory())
    except TypeError as error:
        print('unhashable', name, error)

x = L([1])
x.append(x)
print(repr(x))
print(str([x])[:12])
y = D()
y['self'] = y
print(repr(y))


class Count(list):
    calls = 0

    def append(self, v):
        Count.calls += 1
        super().append(v)


c = Count()
c.append(1)
c.append(2)
print(Count.calls, list(c))


class WithInit(list):
    def __init__(self, it=(), tag='x'):
        super().__init__(it)
        self.tag = tag


w = WithInit([1, 2], tag='t')
print(repr(w), w.tag, len(w))


def refuse(fn):
    try:
        print('ok', repr(fn()))
    except TypeError as error:
        print('TypeError', error)


refuse(lambda: S([1], [2]))
refuse(lambda: S(x=[1]))
refuse(lambda: S(5))
refuse(lambda: F([1], [2]))
refuse(lambda: F(x=1))
refuse(lambda: S.__new__(int))
refuse(lambda: F([1]).copy(1))
refuse(lambda: F([1]).issubset())
refuse(lambda: F([1]).symmetric_difference())

s2 = S([1])
s2.__init__([2, 3])
print(repr(s2))
print(repr(S.__new__(S)), repr(D.__new__(D)), repr(L.__new__(L)), repr(F.__new__(F)))
print([t.__name__ for t in F.__mro__], type(F([1]).copy()).__name__)

# nb_inplace_* return the object itself, so the subclass and any alias survive x += / s |= .
xi = L([1])
yi = xi
xi += [2]
print(type(xi).__name__, xi, yi is xi)
zi = L([1])
zi += 'ab'
print(zi)
try:
    zi += 5
except TypeError as error:
    print('TypeError', error)
ui = S([1, 2])
vi = ui
ui &= {2}
print(type(ui).__name__, repr(ui), vi is ui)
ui |= {3}
ui -= {2}
ui ^= {1}
print(type(ui).__name__, repr(ui))
wi = D({'a': 1})
wi |= {'b': 2}
print(type(wi).__name__, wi)
print('__iadd__' in dir(L), '__ior__' in dir(S), '__ior__' in dir(D))

# dict methods whose self check accepts subclasses
print(D({'a': 1, 'b': 2}).popitem(), end=' ')
dc = D({'a': 1})
dc.clear()
print(dc)
