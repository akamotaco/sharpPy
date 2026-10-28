"""collections.abc support for SharpPy (PEP 3119 abstract base classes).

CPython reference: Lib/_collections_abc.py, Lib/collections/abc.py.

C-vs-C# note (why this is not a literal port of CPython's _check_methods)
------------------------------------------------------------------------
CPython answers "does class C implement dunder X" with the pure-Python
_check_methods(), which walks ``C.__mro__`` and tests ``name in B.__dict__``.
That works because in C every protocol operation is a slot-wrapper *object*
stored in ``type.__dict__`` and inherited from ``object`` (so ``object.__dict__``
contains ``__hash__``/``__eq__``/..., ``list.__dict__['__hash__']`` is literally
None, and ``str.__dict__`` has its own ``__hash__``).

SharpPy implements those operations as CLR behaviour on the PyObject subclasses
(the VM calls GetPythonHash/GetIterator/Length/Contains directly) and does NOT
mirror most of them into ``type.__dict__``: ``hasattr(str, '__hash__')`` is False
and ``dict.__dict__`` has no ``__len__``/``__iter__``. So the string-based
``name in B.__dict__`` probe is unreliable here. Instead _check_methods delegates
one level to the native primitive ``_abc._method_supports(C, name)``, which is the
C#/CLR translation of CPython's per-type slot table (TypeDict scan across the MRO
for user classes and materialised dunders, plus a measured capability table for
the native builtin types). Composite ABCs (Sequence/MutableSequence/Set/
MutableSet/Mapping/MutableMapping/...) keep CPython's design: they are *not*
structural, they rely on real subclassing or ``register()``.
"""

import _abc
from abc import ABCMeta, abstractmethod


def _check_methods(C, *methods):
    # CPython Lib/_collections_abc.py:_check_methods, but delegating the
    # per-dunder "is it present" question to the CLR (see module docstring).
    for method in methods:
        if not _abc._method_supports(C, method):
            return NotImplemented
    return True


def _reg(abc_cls, t):
    # Skip targets that are not real classes on this host (e.g. memoryview is a
    # builtin function here even though its instances have a 'memoryview' type).
    if t is not None and isinstance(t, type):
        abc_cls.register(t)


__all__ = [
    "Hashable", "Awaitable", "Coroutine", "AsyncIterable", "AsyncIterator",
    "AsyncGenerator", "Iterable", "Iterator", "Generator", "Reversible",
    "Sized", "Container", "Callable", "Collection", "Set", "MutableSet",
    "Mapping", "MutableMapping", "MappingView", "KeysView", "ItemsView",
    "ValuesView", "Sequence", "MutableSequence", "ByteString", "Buffer",
]


# --------------------------------------------------------------------------
# Core protocol ABCs: these ARE structural, via __subclasshook__.
# CPython Lib/_collections_abc.py:116-455.
# --------------------------------------------------------------------------

class Hashable(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __hash__(self):
        return NotImplemented

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Hashable:
            return _check_methods(C, "__hash__")
        return NotImplemented


class Iterable(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __iter__(self):
        while False:
            yield None

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Iterable:
            return _check_methods(C, "__iter__")
        return NotImplemented


class Iterator(Iterable):
    __slots__ = ()

    @abstractmethod
    def __next__(self):
        raise StopIteration

    def __iter__(self):
        return self

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Iterator:
            return _check_methods(C, "__iter__", "__next__")
        return NotImplemented


class Reversible(Iterable):
    __slots__ = ()

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Reversible:
            return _check_methods(C, "__reversed__", "__iter__")
        return NotImplemented


class Generator(Iterator):
    __slots__ = ()

    def __next__(self):
        raise StopIteration

    def send(self, value):
        raise StopIteration

    def throw(self, typ, val=None, tb=None):
        raise typ if val is None else typ(val)

    def close(self):
        return None

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Generator:
            return _check_methods(C, "__iter__", "__next__", "send", "throw", "close")
        return NotImplemented


class Sized(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __len__(self):
        return 0

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Sized:
            return _check_methods(C, "__len__")
        return NotImplemented


class Container(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __contains__(self, x):
        return False

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Container:
            return _check_methods(C, "__contains__")
        return NotImplemented


class Callable(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __call__(self, *args, **kwds):
        return False

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Callable:
            return _check_methods(C, "__call__")
        return NotImplemented


class Awaitable(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __await__(self):
        return iter(())

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Awaitable:
            return _check_methods(C, "__await__")
        return NotImplemented


class Coroutine(Awaitable):
    __slots__ = ()

    @abstractmethod
    def send(self, value):
        raise StopIteration

    @abstractmethod
    def throw(self, typ, val=None, tb=None):
        raise typ if val is None else typ(val)

    @abstractmethod
    def close(self):
        return None


class AsyncIterable(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __aiter__(self):
        return self

    @classmethod
    def __subclasshook__(cls, C):
        if cls is AsyncIterable:
            return _check_methods(C, "__aiter__")
        return NotImplemented


class AsyncIterator(AsyncIterable):
    __slots__ = ()

    @abstractmethod
    def __anext__(self):
        raise StopAsyncIteration

    def __aiter__(self):
        return self

    @classmethod
    def __subclasshook__(cls, C):
        if cls is AsyncIterator:
            return _check_methods(C, "__anext__", "__aiter__")
        return NotImplemented


class AsyncGenerator(AsyncIterator):
    __slots__ = ()

    def __anext__(self):
        raise StopAsyncIteration

    def asend(self, value):
        return self.__aiter__().asend(value)


class Buffer(metaclass=ABCMeta):
    __slots__ = ()

    @abstractmethod
    def __buffer__(self, flags):
        raise NotImplementedError

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Buffer:
            return _check_methods(C, "__buffer__")
        return NotImplemented


class Collection(Sized, Iterable, Container):
    __slots__ = ()

    @classmethod
    def __subclasshook__(cls, C):
        if cls is Collection:
            return _check_methods(C, "__len__", "__iter__", "__contains__")
        return NotImplemented


# --------------------------------------------------------------------------
# Concrete ABCs (real subclassing / explicit mixin methods). CPython keeps
# these non-structural except that they inherit Collection's guarded hook,
# which returns NotImplemented for them; membership is via subclassing or
# register(). We keep the concrete mixin behaviour that stdlib modules use.
# --------------------------------------------------------------------------

class Sequence(Reversible, Collection):
    __slots__ = ()

    @abstractmethod
    def __getitem__(self, index):
        raise IndexError

    def __iter__(self):
        i = 0
        try:
            while True:
                v = self[i]
                yield v
                i += 1
        except IndexError:
            return

    def __contains__(self, value):
        for v in self:
            if v is value or v == value:
                return True
        return False

    def __reversed__(self):
        for i in reversed(range(len(self))):
            yield self[i]

    def index(self, value, start=0, stop=None):
        if start is not None and start < 0:
            start = max(len(self) + start, 0)
        if stop is not None and stop < 0:
            stop += len(self)

        i = start
        while stop is None or i < stop:
            try:
                v = self[i]
            except IndexError:
                break
            if v is value or v == value:
                return i
            i += 1
        raise ValueError

    def count(self, value):
        return sum(1 for v in self if v is value or v == value)


class MutableSequence(Sequence):
    __slots__ = ()

    @abstractmethod
    def __setitem__(self, index, value):
        raise ImportError

    @abstractmethod
    def __delitem__(self, index):
        raise ImportError

    @abstractmethod
    def insert(self, index, value):
        raise ImportError

    def append(self, value):
        self.insert(len(self), value)

    def clear(self):
        while True:
            try:
                self.pop()
            except IndexError:
                break

    def reverse(self):
        for i in range(len(self) // 2):
            j = -i - 1
            wi = self[i]
            wj = self[j]
            if wi is wj:
                continue
            self[i], self[j] = self[j], self[i]

    def extend(self, values):
        for value in values:
            self.append(value)

    def pop(self, index=-1):
        try:
            v = self[index]
        except IndexError:
            raise IndexError('pop index out of range')
        del self[index]
        return v

    def remove(self, value):
        try:
            del self[self.index(value)]
        except ValueError:
            raise ValueError(f'{value!r} is not in list')

    def __iadd__(self, values):
        self.extend(values)
        return self


class Set(Collection):
    __slots__ = ()

    def __le__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return len(self - other) == 0

    def __lt__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return len(self) < len(other) and self <= other

    def __ne__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return len(self) != len(other) or not self <= other or not other <= self

    def __eq__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return len(self) == len(other) and self <= other

    def __and__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return self._from_iterable(value for value in self if value in other)

    __rand__ = __and__

    def __or__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return self._from_iterable(list(self) + list(other))

    __ror__ = __or__

    def __sub__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return self._from_iterable(value for value in self if value not in other)

    def __rsub__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return self._from_iterable(value for value in other if value not in self)

    def __xor__(self, other):
        if not isinstance(other, Set):
            return NotImplemented
        return (self - other) | (other - self)

    __rxor__ = __xor__

    def isdisjoint(self, other):
        for value in other:
            if value in self:
                return False
        return True

    @classmethod
    def _from_iterable(cls, it):
        return set(it)


class MutableSet(Set):
    __slots__ = ()

    @abstractmethod
    def add(self, value):
        raise NotImplementedError

    @abstractmethod
    def discard(self, value):
        raise NotImplementedError

    def pop(self):
        for value in self:
            self.discard(value)
            return value
        raise KeyError('set is empty')

    def clear(self):
        for value in list(self):
            self.discard(value)

    def __ior__(self, it):
        for value in it:
            self.add(value)
        return self

    def __iand__(self, it):
        for value in list(self):
            if value not in it:
                self.discard(value)
        return self

    def __ixor__(self, it):
        for value in it:
            if value not in self:
                self.add(value)
            else:
                self.discard(value)
        return self

    def __isub__(self, it):
        for value in it:
            self.discard(value)
        return self


class Mapping(Collection):
    __slots__ = ()

    @abstractmethod
    def __getitem__(self, key):
        raise KeyError

    def get(self, key, default=None):
        try:
            return self[key]
        except KeyError:
            return default

    def __contains__(self, key):
        try:
            self[key]
        except KeyError:
            return False
        else:
            return True

    @abstractmethod
    def __iter__(self):
        raise KeyError

    @abstractmethod
    def __len__(self):
        raise KeyError

    def keys(self):
        return KeysView(self)

    def items(self):
        return ItemsView(self)

    def values(self):
        return ValuesView(self)

    def __eq__(self, other):
        if not isinstance(other, Mapping):
            return NotImplemented
        return dict(self.items()) == dict(other.items())

    __reversed__ = None


class MappingView(Sized):
    __slots__ = '_mapping',

    def __init__(self, mapping):
        self._mapping = mapping

    def __len__(self):
        return len(self._mapping)

    def __repr__(self):
        return f'{self.__class__.__name__}({self._mapping!r})'


class KeysView(MappingView, Set):
    __slots__ = ()

    def __contains__(self, key):
        return key in self._mapping

    def __iter__(self):
        yield from self._mapping


class ItemsView(MappingView, Set):
    __slots__ = ()

    def __contains__(self, item):
        key, value = item
        try:
            v = self._mapping[key]
        except KeyError:
            return False
        else:
            return v is value or v == value

    def __iter__(self):
        for key in self._mapping:
            yield (key, self._mapping[key])


class ValuesView(MappingView, Collection):
    __slots__ = ()

    def __contains__(self, value):
        for key in self._mapping:
            v = self._mapping[key]
            if v is value or v == value:
                return True
        return False

    def __iter__(self):
        for key in self._mapping:
            yield self._mapping[key]


class MutableMapping(Mapping):
    __slots__ = ()

    @abstractmethod
    def __setitem__(self, key, value):
        raise KeyError

    @abstractmethod
    def __delitem__(self, key):
        raise KeyError

    __marker = object()

    def pop(self, key, default=__marker):
        try:
            value = self[key]
        except KeyError:
            if default is self.__marker:
                raise
            return default
        else:
            del self[key]
            return value

    def popitem(self):
        try:
            key = next(iter(self))
        except StopIteration:
            raise KeyError from None
        value = self[key]
        del self[key]
        return key, value

    def clear(self):
        try:
            while True:
                self.popitem()
        except KeyError:
            pass

    def update(self, other=(), **kwds):
        if isinstance(other, Mapping):
            for key in other:
                self[key] = other[key]
        elif hasattr(other, "keys"):
            for key in other.keys():
                self[key] = other[key]
        else:
            for key, value in other:
                self[key] = value
        for key, value in kwds.items():
            self[key] = value

    def setdefault(self, key, default=None):
        try:
            return self[key]
        except KeyError:
            self[key] = default
        return default


class ByteString(Sequence):
    __slots__ = ()


# --------------------------------------------------------------------------
# Virtual-subclass registration of the builtin concrete types.
# CPython Lib/_collections_abc.py registers these explicitly (they are C types
# that don't inherit from the ABCs); register() also propagates to ancestors.
# --------------------------------------------------------------------------

_reg(Generator, type((i for i in ())))

_reg(Sequence, tuple)
_reg(Sequence, str)
_reg(Sequence, type(range(0)))
_reg(Sequence, type(memoryview(b"")))

_reg(MutableSequence, list)
_reg(MutableSequence, bytearray)

_reg(ByteString, bytes)
_reg(ByteString, bytearray)

_reg(Set, frozenset)
_reg(MutableSet, set)

_reg(Mapping, dict)
_reg(MutableMapping, dict)

_reg(KeysView, type({}.keys()))
_reg(ItemsView, type({}.items()))
_reg(ValuesView, type({}.values()))
