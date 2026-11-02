class It:
    def __init__(self, mode=None): self.mode = mode; self.calls = []; self.left = 2
    def __iter__(self): self.calls.append('iter'); return self
    def __next__(self):
        self.calls.append('next')
        if self.left == 0: raise StopIteration
        self.left -= 1
        return 65
    def __length_hint__(self):
        self.calls.append('hint')
        if self.mode == 'value': raise ValueError('hint')
        if self.mode == 'type': raise TypeError('hint')
        if self.mode == 'neg': return -1
        if self.mode == 'str': return 'x'
        if self.mode == 'ni': return NotImplemented
        return 2

class Ln(It):
    def __len__(self):
        self.calls.append('len')
        if self.mode == 'lenv': raise ValueError('len')
        if self.mode == 'lent': raise TypeError('len')
        return 2

def f(*a): return a
def g(**k): return k
def join(x):
    return ''.join(chr(c) if isinstance(c, int) else c for c in x)
def unpack(x):
    a, *b = x
    return a, b
def extendl(x):
    l = []
    l.extend(x)
    return l
def iaddl(x):
    l = []
    l += x
    return l
def extendb(x):
    b = bytearray()
    b.extend(x)
    return b
def iaddb(x):
    b = bytearray()
    b += x
    return b
def strjoin(x):
    return ','.join(x)
def starl(x): return [*x]
def start(x): return (*x,)
def stars(x): return {*x}
def call(x): return f(*x)
import collections, itertools, operator
cases = dict(list=list, tuple=tuple, bytes=bytes, bytearray=bytearray, set=set, frozenset=frozenset, sorted=sorted,
             extendl=extendl, iaddl=iaddl, extendb=extendb, iaddb=iaddb, starl=starl, start=start, stars=stars, call=call,
             unpack=unpack, strjoin=strjoin, sum=sum, deque=collections.deque,
             reversed=lambda x: None, map=lambda x: list(map(lambda v: v, x)), zip=lambda x: list(zip(x)),
             hint=operator.length_hint, enumerate=lambda x: list(enumerate(x)), chain=lambda x: list(itertools.chain(x)),
             dict=lambda x: dict.fromkeys(x))
for name, fn in cases.items():
    for cls in It, Ln:
        for mode in None, 'value', 'type', 'neg', 'str', 'ni', 'lenv', 'lent':
            o = cls(mode)
            try:
                r = repr(fn(o))
            except Exception as e:
                r = type(e).__name__ + ':' + str(e)
            print(name, cls.__name__, mode, r, o.calls)
