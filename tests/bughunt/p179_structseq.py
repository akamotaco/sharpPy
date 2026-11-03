"""Objects/structseq.c: one type behind sys.version_info and its kin, and aiter/anext."""
import pickle
import sys
import time

for s in (sys.version_info, sys.float_info, sys.int_info, sys.hash_info, sys.flags, sys.thread_info, time.gmtime(0)):
    t = type(s)
    print(t, t.__name__, t.__module__, t.__mro__, isinstance(s, tuple), len(s) == t.n_sequence_fields)
    print(t.n_fields, t.n_sequence_fields, t.n_unnamed_fields, t.__match_args__)
    print(sorted(k for k in vars(t)))
    print(type(vars(t)[t.__match_args__[0]]).__name__, vars(t)[t.__match_args__[0]])
    print(s[0], s[-1], s[:2], tuple(s) == s, hash(s) == hash(tuple(s)))
    print(s.__reduce__() if t is type(time.gmtime(0)) else None)
    try:
        s.__setattr__(t.__match_args__[0], 1)
    except Exception as error:
        print(type(error).__name__, error)
    try:
        setattr(s, 'nope', 1)
    except Exception as error:
        print(type(error).__name__, error)
    try:
        class X(t):
            pass
    except Exception as error:
        print(type(error).__name__, error)
    try:
        print(t(tuple(s)) == s)
    except Exception as error:
        print(type(error).__name__, error)
    try:
        print(pickle.loads(pickle.dumps(s)) == s)
    except Exception as error:
        print(type(error).__name__, error)
    print(s + ('x',) == tuple(s) + ('x',), s * 2 == tuple(s) * 2, 'x' in s, list(s) == list(tuple(s)))
print(repr(sys.float_info), repr(sys.int_info), repr(sys.hash_info))
print(repr(sys.flags).replace('dont_write_bytecode=1', 'dont_write_bytecode=0').replace('no_site=1', 'no_site=0').replace('utf8_mode=1', 'utf8_mode=0'))

st = time.struct_time
for args in ((), (1,), ((1,),), (range(9),), (range(11),), (range(12),), ([1] * 9, {'tm_zone': 'z'}), ([1] * 9, 1), (1, 2),
             (1, 2, 3)):
    try:
        x = st(*args)
        print(repr(x), x.tm_zone, x.tm_gmtoff, x.__reduce__())
    except Exception as error:
        print(type(error).__name__, error)
print(st(sequence=range(9)), st(sequence=range(9), dict={'tm_gmtoff': 1}).tm_gmtoff)
try:
    st(range(9), x=1)
except TypeError as error:
    print(error)
match time.gmtime(0):
    case (a, b, *_):
        print(a, b)
VI = type(sys.version_info)
match sys.version_info:
    case VI(major=m, minor=n):
        print(m, n)
match time.gmtime(0):
    case time.struct_time(y, mo):
        print(y, mo)
print(time.asctime(st((2000, 2, 3, 4, 5, 6, 3, 34, 0))), time.mktime(time.gmtime(90000)) - time.mktime(time.gmtime(86400)))
print(sys.hexversion, hex(sys.implementation.hexversion), sys.api_version, sys.is_finalizing(), sys.get_asyncgen_hooks())
print(sys.call_tracing(max, (1, 2)), sys._getframemodulename(), sys._getframemodulename(99))
print(list(sys._current_frames().values())[0].f_code.co_name)


class It:
    def __aiter__(self):
        return self
    async def __anext__(self):
        raise StopAsyncIteration


async def gen():
    yield 1
    yield 2


def run(coroutine):
    try:
        coroutine.send(None)
    except StopIteration as stop:
        return stop.value


async def main():
    g = aiter(gen())
    print(type(g).__name__, await anext(g), await anext(g), await anext(g, 'end'), await anext(g, None))
    try:
        await anext(g)
    except StopAsyncIteration:
        print('stop')
    print(await anext(It(), 7))
    awaitable = anext(It(), 7)
    await awaitable
    print(type(awaitable).__name__, awaitable.__await__() is awaitable, iter(awaitable) is awaitable)
    print(await anext(gen(), None))
    a = anext(gen(), 8)
    print(a.send(None) if False else 'skip')

print(run(main()))
for call in (lambda: aiter(1), lambda: anext(1), lambda: aiter(), lambda: anext(), lambda: anext(It(), 1, 2), lambda: aiter(1, 2),
             lambda: aiter(type('A', (), {'__aiter__': lambda s: 1})())):
    try:
        print(call())
    except TypeError as error:
        print(error)

log = []
sys.set_asyncgen_hooks(firstiter=lambda g: log.append(('first', g.__name__)), finalizer=lambda g: log.append('final'))
g = gen()
log.append('made')
a = g.__anext__()
log.append('asked')
print(log, sys.get_asyncgen_hooks().firstiter is not None)
sys.set_asyncgen_hooks(None, None)
print(sys.get_asyncgen_hooks())
for call in (lambda: sys.set_asyncgen_hooks(1), lambda: sys.set_asyncgen_hooks(finalizer=1), lambda: sys.set_asyncgen_hooks(x=1)):
    try:
        print(call())
    except TypeError as error:
        print(error)
for name in ('NoneType', 'function'):
    print(hasattr(__builtins__, name))


# The attributes of a builtin object are the type's: type(x).name, vars(type(x)) and x.name agree.
import re
import types
import itertools
import operator
import collections


def f(a, b=1):
    return a


for obj, names in ((f.__code__, ('co_name', 'co_argcount', 'co_varnames', 'co_lines', 'replace')),
                   (sys._getframe(), ('f_code', 'f_lineno', 'f_back', 'clear')),
                   ((lambda x: (lambda: x))(1).__closure__[0], ('cell_contents',)),
                   (range(3), ('start', 'stop', 'step')), (slice(1), ('start', 'stop', 'step')),
                   (memoryview(b'ab'), ('format', 'itemsize', 'shape', 'obj', 'readonly', 'f_contiguous', 'toreadonly')),
                   (re.compile('a'), ('pattern', 'flags', 'groups', 'groupindex', 'match', 'sub')),
                   (re.match('a', 'a'), ('string', 're', 'pos', 'regs', 'group', 'span')),
                   (types.MappingProxyType({}), ('keys', 'get')), (int | str, ('__args__', '__parameters__')),
                   (list[int], ('__args__', '__origin__', '__parameters__'))):
    t = type(obj)
    for name in names:
        print(t.__name__, name, type(vars(t)[name]).__name__, repr(getattr(t, name)).split(' at ')[0], name in dir(obj), hasattr(obj, name))

ns = types.SimpleNamespace(a=1)
ns.me = ns
class Sub(types.SimpleNamespace):
    def hi(self):
        return self.a
print(repr(ns), ns.__dict__ is ns.__dict__ or ns.__dict__ == ns.__dict__, Sub(a=3).hi(), repr(Sub(a=3)), Sub(a=3) == types.SimpleNamespace(a=3))
ns.__dict__['z'] = 1
del ns.me
print(ns, ns.z, types.SimpleNamespace.__hash__, ns.__reduce__())
for call in (lambda: types.SimpleNamespace(1), lambda: hash(ns), lambda: ns.nope, lambda: ns < ns):
    try:
        print(call())
    except Exception as error:
        print(type(error).__name__)
print(operator.attrgetter('a')(ns), operator.methodcaller('upper')('a'), operator.itemgetter(1)((1, 2)), type(operator.itemgetter))
for it in (itertools.count(), itertools.chain([1]), itertools.chain.from_iterable([[1]]), itertools.repeat(1), itertools.islice([], 1)):
    print(type(it), type(it).__mro__, isinstance(it, type(it)))
print(itertools.chain[int], list(itertools.chain.from_iterable([[1], [2]])))
print(collections.deque([1]) + collections.deque([2]), collections.deque[int], enumerate[int], types.MappingProxyType[int])
print(True.__rand__(1), True.__ror__(0), bytearray(b'a').__imul__(2), None.__repr__(), Ellipsis.__repr__())
b = bytearray(b'a')
c = b
b += b'x'
b *= 2
print(c, c is b)
class Meta(type):
    def __instancecheck__(cls, x):
        return super().__instancecheck__(x) or x == 1
    def __subclasscheck__(cls, c):
        return super().__subclasscheck__(c) or c is int
class WithMeta(metaclass=Meta):
    pass
print(isinstance(1, WithMeta), isinstance(2, WithMeta), issubclass(int, WithMeta), issubclass(str, WithMeta))
class Slotted(tuple):
    __slots__ = ()
for x in (Slotted(), collections.namedtuple('N', 'a')(1), collections.defaultdict()):
    print(hasattr(x, '__dict__'), '__dict__' in dir(x), '__weakref__' in dir(x))
print((lambda: 0).__call__(), type((lambda: 0).__call__).__name__, type((lambda: 0).__get__).__name__, (lambda self: self).__get__(1)(), repr(type(f.__code__).co_name))
