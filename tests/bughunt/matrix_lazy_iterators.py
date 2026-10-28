def show(k, fn):
    try:
        print(f"{k!r}: {fn()}")
    except Exception as e:
        print(f"{k!r}: EXC {type(e).__name__}: {e}")

# zip strict messages (both directions, 3-way)
show("zip.strict.short", lambda: list(zip([1, 2], [1], strict=True)))
show("zip.strict.long", lambda: list(zip([1], [1, 2], strict=True)))
show("zip.strict.3", lambda: list(zip([1, 2, 3], [1, 2], [1], strict=True)))
show("zip.strict.ok", lambda: list(zip([1, 2], [3, 4], strict=True)))
show("zip.strict.firstempty", lambda: list(zip([], [1], strict=True)))
show("zip.notype", lambda: list(zip([1, 2], [1])))
# map laziness and multi-iterable padding
log = []
def f(x):
    log.append(x)
    return x * 2
show("map.lazy", lambda: (list(iter(f(x) for x in [1])), None))
m = map(f, [1, 2, 3])
show("map.created", lambda: (len(log), repr(m)[:5]))
show("map.first", lambda: next(m))
show("map.after1", lambda: len(log))
show("map.rest", lambda: list(m))
show("map.done", lambda: len(log))
show("map.multi", lambda: list(map(lambda a, b: a + b, [1, 2, 3], [10])))
show("map.none", lambda: list(map(None, [1, 2], [3, 4])))
show("map.stopleak", lambda: (next(map(str, [])), 1))
show("map.type", lambda: type(map(str, [])).__name__)
show("zip.type", lambda: type(zip([])).__name__)
show("filter.type", lambda: type(filter(None, [])).__name__)
# filter lazy
shown = []
show("filter.lazy", lambda: [shown.append(x) or x for x in filter(lambda v: v > 1, [1, 2, 3])])
show("filter.none", lambda: list(filter(None, [0, 1, "", 2])))
# iter(callable, sentinel)
vals = iter([1, 2, 0, 5])
show("iter.sentinel", lambda: list(iter(lambda: next(vals), 0)))
vals2 = iter([1, 2, 0, 5])
it2 = iter(lambda: next(vals2), 0)
show("iter.sentinel.type", lambda: type(it2).__name__)
show("iter.sentinel2", lambda: [next(it2), next(it2)])
show("iter.sentinel.stop", lambda: next(it2))
class Raise:
    def __init__(self): self.n = 0
    def __call__(self):
        self.n += 1
        if self.n == 2: raise ValueError("boom")
        return self.n
show("iter.sentinel.exc", lambda: list(iter(Raise(), 99)))
show("iter.zero", lambda: iter())
show("iter.toofew", lambda: iter(1, 2, 3))
# reversed
show("reversed.range", lambda: list(reversed(range(3))))
show("reversed.str", lambda: list(reversed("abc")))
show("reversed.set", lambda: list(reversed({1})))
show("reversed.int", lambda: list(reversed(5)))
show("reversed.dict", lambda: list(reversed({'a': 1, 'b': 2})))
class GSeq:
    def __getitem__(self, i):
        if i >= 3: raise IndexError
        return i * i
    def __len__(self): return 3
show("reversed.getitem", lambda: list(reversed(GSeq())))
class NoLen:
    def __getitem__(self, i):
        if i >= 2: raise IndexError
        return i
show("reversed.nolen", lambda: list(reversed(NoLen())))
# tuple/list index messages
show("tuple.index.miss", lambda: (1, 2).index(5))
show("list.index.miss", lambda: [1, 2].index(5))
show("tuple.index.str.miss", lambda: ().index("z"))
# in with __getitem__ fallback
class SeqLike:
    def __getitem__(self, i):
        if i >= 3: raise IndexError
        return i * 10
show("in.getitem.hit", lambda: 20 in SeqLike())
show("in.getitem.miss", lambda: 25 in SeqLike())
class BadItem:
    def __getitem__(self, i):
        if i == 0: return 7
        raise RuntimeError("stop via error")
show("in.getitem.exc", lambda: 1 in BadItem())
show("for.getitem", lambda: [x for x in SeqLike()])
show("list.getitem", lambda: list(SeqLike()))
# set operators/update
show("set.or.list", lambda: {1} | [2])
show("list.or.set", lambda: [1] | {2})
show("set.and.list", lambda: {1} & [1, 2])
show("set.minus.list", lambda: {1, 2} - [1])
show("set.update.multi", lambda: sorted(set().update([0], [1, 2]) or set([0, 1, 2])))
s = set()
show("set.update.2", lambda: (s.update([1], (2, 3)), sorted(s))[1])
show("set.update.none", lambda: (set().update(None), 1))
