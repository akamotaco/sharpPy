# p21 - collections.abc parity (structural protocol + registration + mixins)
# CPython 3.12 reference.
import collections.abc as ca
from collections.abc import (
    Hashable, Iterable, Iterator, Sized, Container, Callable, Collection,
    Reversible, Sequence, MutableSequence, Set, MutableSet, Mapping,
    MutableMapping, Generator,
)


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, e)


# --- structural protocol ABCs over builtins (representative) ---
show("hash-int", lambda: isinstance(5, Hashable))
show("hash-list", lambda: isinstance([], Hashable))
show("hash-set", lambda: isinstance(set(), Hashable))
show("iter-list", lambda: isinstance([], Iterable))
show("iter-int", lambda: isinstance(5, Iterable))
show("iter-gen", lambda: isinstance((i for i in ()), Iterable))
show("sized-str", lambda: isinstance("x", Sized))
show("sized-gen", lambda: isinstance((i for i in ()), Sized))
show("cont-str", lambda: isinstance("x", Container))
show("cont-gen", lambda: isinstance((i for i in ()), Container))
show("call-type", lambda: isinstance(int, Callable))
show("call-func", lambda: isinstance((lambda: 0), Callable))
show("call-int", lambda: isinstance(5, Callable))
show("rev-dict", lambda: isinstance({}, Reversible))
show("rev-set", lambda: isinstance(set(), Reversible))
show("coll-list", lambda: isinstance([], Collection))
show("coll-gen", lambda: isinstance((i for i in ()), Collection))

# --- composite ABCs via register()/subclassing ---
show("seq-list", lambda: isinstance([], Sequence))
show("seq-tuple", lambda: isinstance((), Sequence))
show("seq-dict", lambda: isinstance({}, Sequence))
show("seq-range", lambda: isinstance(range(3), Sequence))
show("seq-mem", lambda: isinstance(memoryview(b"x"), Sequence))
show("mseq-list", lambda: isinstance([], MutableSequence))
show("mseq-tuple", lambda: isinstance((), MutableSequence))
show("set-frozenset", lambda: isinstance(frozenset(), Set))
show("mset-set", lambda: isinstance(set(), MutableSet))
show("mset-frozenset", lambda: isinstance(frozenset(), MutableSet))
show("map-dict", lambda: isinstance({}, Mapping))
show("mmap-dict", lambda: isinstance({}, MutableMapping))
show("gen-gen", lambda: isinstance((i for i in ()), Generator))
show("gen-iter", lambda: isinstance(iter([]), Generator))

# --- register() propagates to ancestor ABCs (CPython behaviour) ---
class X:
    pass


MutableSet.register(X)
show("x-mset", lambda: isinstance(X(), MutableSet))
show("x-set", lambda: isinstance(X(), Set))
show("x-collection", lambda: isinstance(X(), Collection))
show("x-sized", lambda: isinstance(X(), Sized))
show("x-iterable", lambda: isinstance(X(), Iterable))
show("x-seq", lambda: isinstance(X(), Sequence))

# --- user duck-type structural checks ---
class MyIter:
    def __iter__(self):
        return iter([1, 2])


show("myiter-iterable", lambda: isinstance(MyIter(), Iterable))
show("myiter-iterator", lambda: isinstance(MyIter(), Iterator))
show("myiter-seq", lambda: isinstance(MyIter(), Sequence))
show("myiter-collection", lambda: isinstance(MyIter(), Collection))


class MyFull:
    def __iter__(self):
        return iter([])

    def __next__(self):
        raise StopIteration

    def __len__(self):
        return 0

    def __contains__(self, x):
        return False


show("myfull-iterator", lambda: isinstance(MyFull(), Iterator))
show("myfull-collection", lambda: isinstance(MyFull(), Collection))

# --- user subclass of ABC + mixin methods ---
class MySeq(MutableSequence):
    def __init__(self):
        self.data = []

    def __getitem__(self, i):
        return self.data[i]

    def __setitem__(self, i, v):
        self.data[i] = v

    def __delitem__(self, i):
        del self.data[i]

    def insert(self, i, v):
        self.data.insert(i, v)

    def __len__(self):
        return len(self.data)


show("myseq-isseq", lambda: isinstance(MySeq(), Sequence))
show("myseq-ismseq", lambda: isinstance(MySeq(), MutableSequence))
s = MySeq()
s.append(10)
s.extend([20, 30])
show("myseq-append-extend", lambda: list(s))
show("myseq-index", lambda: s.index(20))
show("myseq-count", lambda: s.count(10))
show("myseq-pop", lambda: (s.pop(), list(s)))
show("myseq-remove", lambda: (s.remove(20), list(s)))
show("myseq-contains", lambda: 30 in s)
show("myseq-reversed", lambda: list(reversed(s)))


class MyMap(MutableMapping):
    def __init__(self):
        self.d = {}

    def __getitem__(self, k):
        return self.d[k]

    def __setitem__(self, k, v):
        self.d[k] = v

    def __delitem__(self, k):
        del self.d[k]

    def __iter__(self):
        return iter(self.d)

    def __len__(self):
        return len(self.d)


m = MyMap()
m["a"] = 1
m["b"] = 2
show("mymap-ismap", lambda: isinstance(m, Mapping))
show("mymap-get", lambda: (m.get("a"), m.get("z"), m.get("z", 99)))
show("mymap-keys", lambda: sorted(m.keys()))
show("mymap-items", lambda: sorted(m.items()))
show("mymap-values", lambda: sorted(m.values()))
show("mymap-setdefault", lambda: (m.setdefault("c", 3), m["c"]))
show("mymap-pop", lambda: (m.pop("a"), "a" in m))
show("mymap-contains", lambda: ("b" in m, "z" in m))
show("mymap-update", lambda: (m.update({"d": 4}), sorted(m.keys())))

# --- set algebra on a user subclass ---
class MySet(Set):
    def __init__(self, items):
        self._items = set(items)

    def __contains__(self, x):
        return x in self._items

    def __iter__(self):
        return iter(self._items)

    def __len__(self):
        return len(self._items)

    @classmethod
    def _from_iterable(cls, it):
        return MySet(it)


a = MySet([1, 2, 3])
b = MySet([3, 4])
show("myset-isset", lambda: isinstance(a, Set))
show("myset-issubset", lambda: MySet([1, 2]) <= a)
show("myset-union", lambda: sorted(a | b))
show("myset-inter", lambda: sorted(a & b))
show("myset-diff", lambda: sorted(a - b))
show("myset-xor", lambda: sorted(a ^ b))
show("myset-disjoint", lambda: (a.isdisjoint(MySet([9])), a.isdisjoint(MySet([3]))))

# --- abstract instantiation guard ---
class AbstractThing(metaclass=type(MutableSet)):
    pass


show("iter-abstract-exc", lambda: Iterable())
show("mset-abstract-exc", lambda: MutableSet())

# --- __all__ surface ---
show("all-count", lambda: len(ca.__all__))
show("all-has", lambda: all(n in ca.__all__ for n in
                            ["Hashable", "Iterable", "Sequence", "Mapping",
                             "Callable", "Set", "MutableSet", "MutableMapping",
                             "MutableSequence", "Reversible", "Generator",
                             "Awaitable", "Coroutine", "AsyncIterable",
                             "AsyncIterator", "Buffer", "ByteString",
                             "Collection", "Container", "Sized", "Iterator",
                             "MappingView", "KeysView", "ItemsView", "ValuesView"]))

# --- continuity with io ABCs (p19/p20), via in-memory streams (no filesystem) ---
import io

show("sio-iobase", lambda: isinstance(io.StringIO(), io.IOBase))
show("sio-textbase", lambda: isinstance(io.StringIO(), io.TextIOBase))
show("bio-buffered", lambda: isinstance(io.BytesIO(), io.BufferedIOBase))
show("bio-not-raw", lambda: isinstance(io.BytesIO(), io.RawIOBase))
show("bio-iobase", lambda: isinstance(io.BytesIO(), io.IOBase))
show("str-not-textbase", lambda: isinstance("x", io.TextIOBase))
