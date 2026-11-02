import collections
class M:
    def hi(self): return 'hi'
bases = [dict, list, tuple, set, frozenset, str, bytes, bytearray, int, float, complex, Exception, ValueError, OSError, collections.OrderedDict, collections.defaultdict, collections.deque]
for b in bases:
    for name, bs in (('M,b', (M, b)), ('b,M', (b, M))):
        try:
            C = type('C', bs, {})
            o = C()
            print(b.__name__, name, type(o).__name__, isinstance(o, b), repr(o), o.hi())
        except Exception as e:
            print(b.__name__, name, 'ERR', type(e).__name__, e)
class M2:
    def __init__(self, *a, **k):
        super().__init__(*a, **k)
class D(M2, dict): pass
print(D(a=1))
class L(M2, list): pass
print(L([1,2]), len(L([1])))
class S(M2, str): pass
print(repr(S('x')))
class I(M, int): pass
print(I(3) + 1, I(3).hi())
