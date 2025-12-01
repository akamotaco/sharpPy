"""
Minimal _collections_abc module for SharpPy
Provides abstract base classes for collections

CPython 3.12: Lib/_collections_abc.py
"""

# _check_methods function for ABC registration
# CPython 3.12: Lib/_collections_abc.py:115-128
def _check_methods(C, *methods):
    """Check if class C has all the specified methods."""
    mro = C.__mro__
    for method in methods:
        for B in mro:
            if method in B.__dict__:
                if B.__dict__[method] is None:
                    return NotImplemented
                break
        else:
            return NotImplemented
    return True


### MAPPINGS ###
# CPython 3.12: Lib/_collections_abc.py:787-838

class Mapping:
    """A Mapping is a generic container for associating key/value pairs.

    CPython 3.12: Lib/_collections_abc.py:787-838

    This class provides concrete generic implementations of all
    methods except for __getitem__, __iter__, and __len__.
    """

    __slots__ = ()

    def __getitem__(self, key):
        raise KeyError

    def get(self, key, default=None):
        'D.get(k[,d]) -> D[k] if k in D, else d.  d defaults to None.'
        # CPython 3.12: Lib/_collections_abc.py:804-809
        try:
            return self[key]
        except KeyError:
            return default

    def __contains__(self, key):
        # CPython 3.12: Lib/_collections_abc.py:811-817
        try:
            self[key]
        except KeyError:
            return False
        else:
            return True

    def keys(self):
        "D.keys() -> a set-like object providing a view on D's keys"
        # CPython 3.12: Lib/_collections_abc.py:819-820
        return KeysView(self)

    def items(self):
        "D.items() -> a set-like object providing a view on D's items"
        # CPython 3.12: Lib/_collections_abc.py:822-823
        return ItemsView(self)

    def values(self):
        "D.values() -> an object providing a view on D's values"
        # CPython 3.12: Lib/_collections_abc.py:825-826
        return ValuesView(self)

    def __eq__(self, other):
        # CPython 3.12: Lib/_collections_abc.py:831-834
        if not isinstance(other, Mapping):
            return NotImplemented
        return dict(self.items()) == dict(other.items())

    def __iter__(self):
        # Subclasses must implement
        return iter(())

    def __len__(self):
        # Subclasses must implement
        return 0

    __reversed__ = None


# Simplified MappingView classes for Mapping.keys(), .items(), .values()
# CPython 3.12: Lib/_collections_abc.py:841-916

class MappingView:
    """Base class for mapping views."""
    # CPython 3.12: Lib/_collections_abc.py:841-854

    __slots__ = ('_mapping',)

    def __init__(self, mapping):
        self._mapping = mapping

    def __len__(self):
        return len(self._mapping)

    def __repr__(self):
        return '{0.__class__.__name__}({0._mapping!r})'.format(self)


class KeysView(MappingView):
    """A set-like object providing a view on D's keys."""
    # CPython 3.12: Lib/_collections_abc.py:857-869

    __slots__ = ()

    def __contains__(self, key):
        return key in self._mapping

    def __iter__(self):
        yield from self._mapping


class ItemsView(MappingView):
    """A set-like object providing a view on D's items."""
    # CPython 3.12: Lib/_collections_abc.py:875-894

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


class ValuesView(MappingView):
    """An object providing a view on D's values."""
    # CPython 3.12: Lib/_collections_abc.py:900-913

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
    """A MutableMapping is a generic container for associating key/value pairs.

    CPython 3.12: Lib/_collections_abc.py:919-1001

    This class provides concrete generic implementations of all
    methods except for __getitem__, __setitem__, __delitem__,
    __iter__, and __len__.
    """

    __slots__ = ()

    def __setitem__(self, key, value):
        raise KeyError

    def __delitem__(self, key):
        raise KeyError

    __marker = object()

    def pop(self, key, default=__marker):
        '''D.pop(k[,d]) -> v, remove specified key and return the corresponding value.
          If key is not found, d is returned if given, otherwise KeyError is raised.
        '''
        # CPython 3.12: Lib/_collections_abc.py:940-952
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
        '''D.popitem() -> (k, v), remove and return some (key, value) pair
           as a 2-tuple; but raise KeyError if D is empty.
        '''
        # CPython 3.12: Lib/_collections_abc.py:954-964
        try:
            key = next(iter(self))
        except StopIteration:
            raise KeyError from None
        value = self[key]
        del self[key]
        return key, value

    def clear(self):
        'D.clear() -> None.  Remove all items from D.'
        # CPython 3.12: Lib/_collections_abc.py:966-972
        try:
            while True:
                self.popitem()
        except KeyError:
            pass

    def update(self, other=(), **kwds):
        ''' D.update([E, ]**F) -> None.  Update D from mapping/iterable E and F.
            If E present and has a .keys() method, does:     for k in E.keys(): D[k] = E[k]
            If E present and lacks .keys() method, does:     for (k, v) in E: D[k] = v
            In either case, this is followed by: for k, v in F.items(): D[k] = v
        '''
        # CPython 3.12: Lib/_collections_abc.py:974-990
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
        'D.setdefault(k[,d]) -> D.get(k,d), also set D[k]=d if k not in D'
        # CPython 3.12: Lib/_collections_abc.py:992-998
        try:
            return self[key]
        except KeyError:
            self[key] = default
        return default


### SEQUENCES ###
# CPython 3.12: Lib/_collections_abc.py:1006-1067

# Minimal Sequence ABC for random.py
class Sequence:
    """Abstract base class for sized, iterable containers that support indexing."""

    __slots__ = ()

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
            if v == value or v is value:
                return True
        return False

    def __reversed__(self):
        for i in reversed(range(len(self))):
            yield self[i]

    def index(self, value, start=0, stop=None):
        if start < 0:
            start = max(len(self) + start, 0)
        if stop is None:
            stop = len(self)
        elif stop < 0:
            stop = max(len(self) + stop, 0)

        for i in range(start, stop):
            if self[i] == value or self[i] is value:
                return i
        raise ValueError(f'{value!r} is not in sequence')

    def count(self, value):
        return sum(1 for v in self if v == value or v is value)
