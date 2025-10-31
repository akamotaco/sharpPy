"""collections.py - High-performance container datatypes

CPython 3.12 compatible - Container datatypes
Uses _collections C extension module for performance-critical operations
"""

__all__ = ['deque', 'defaultdict', 'namedtuple', 'UserDict', 'UserList',
           'UserString', 'Counter', 'OrderedDict', 'ChainMap']

# Import from _collections C extension
try:
    from _collections import deque, defaultdict
except ImportError:
    # Fallback implementations if _collections is not available
    pass

# Counter - dict subclass for counting hashable objects
class Counter(dict):
    """Dict subclass for counting hashable items.

    CPython 3.12 compatible - simplified implementation
    """

    def __init__(self, iterable=None, **kwds):
        """Create a new, empty Counter object or update from iterable/kwds"""
        super().__init__()
        self.update(iterable, **kwds)

    def update(self, iterable=None, **kwds):
        """Like dict.update() but add counts instead of replacing them"""
        if iterable is not None:
            if isinstance(iterable, dict):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) + count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) + 1

        if kwds:
            self.update(kwds)

    def most_common(self, n=None):
        """List the n most common elements and their counts from most common to least.

        If n is None, list all element counts.
        """
        # Simple implementation without heapq (simplified)
        items = list(self.items())
        # Sort by count (descending), then by key (ascending)
        items.sort(key=lambda x: (-x[1], str(x[0])))

        if n is None:
            return items
        return items[:n]

    def elements(self):
        """Iterator over elements repeating each as many times as its count.

        Elements returned in arbitrary order. If count < 1, elements() omits it.
        """
        for elem, count in self.items():
            for _ in range(int(count)):
                yield elem

    def __repr__(self):
        if not self:
            return f'{self.__class__.__name__}()'

        items = ', '.join(f'{k!r}: {v!r}' for k, v in self.items())
        return f'{self.__class__.__name__}({{{items}}})'


# namedtuple - Factory function for creating tuple subclasses with named fields
def namedtuple(typename, field_names, *, rename=False, defaults=None, module=None):
    """Returns a new subclass of tuple with named fields.

    CPython 3.12 compatible - simplified stub implementation
    typename: name of the new class
    field_names: sequence of field names or whitespace/comma-separated string
    """
    # Parse field_names
    if isinstance(field_names, str):
        # Split by whitespace or comma
        field_names = field_names.replace(',', ' ').split()

    field_names = list(field_names)

    # Simple stub - return a basic tuple subclass
    # Full implementation would generate a class with __new__, __repr__, _fields, etc.
    class NamedTuple(tuple):
        _fields = tuple(field_names)

        def __new__(cls, *args):
            return tuple.__new__(cls, args)

        def __repr__(self):
            items = ', '.join(f'{name}={val!r}' for name, val in zip(self._fields, self))
            return f'{typename}({items})'

    NamedTuple.__name__ = typename
    NamedTuple.__qualname__ = typename

    return NamedTuple


# OrderedDict - Dict subclass that remembers insertion order
class OrderedDict(dict):
    """Dictionary that remembers insertion order.

    CPython 3.12: Regular dicts maintain insertion order since Python 3.7,
    but OrderedDict is still provided for compatibility and some specific methods.

    Simplified stub implementation.
    """

    def __init__(self, *args, **kwds):
        """Initialize an ordered dictionary."""
        super().__init__(*args, **kwds)

    def __repr__(self):
        if not self:
            return f'{self.__class__.__name__}()'
        items = ', '.join(f'({k!r}, {v!r})' for k, v in self.items())
        return f'{self.__class__.__name__}([{items}])'


# ChainMap - Combine multiple mappings for sequential lookup
class ChainMap:
    """ChainMap groups multiple dicts or other mappings together to create a single, updateable view.

    Simplified stub implementation.
    """

    def __init__(self, *maps):
        """Initialize a ChainMap by setting maps to the given mappings."""
        self.maps = list(maps) if maps else [{}]

    def __getitem__(self, key):
        for mapping in self.maps:
            try:
                return mapping[key]
            except KeyError:
                pass
        raise KeyError(key)

    def __setitem__(self, key, value):
        self.maps[0][key] = value

    def __repr__(self):
        return f'{self.__class__.__name__}({", ".join(map(repr, self.maps))})'


# UserDict - Wrapper around dictionary objects for easier dict subclassing
class UserDict:
    """Simplified stub for UserDict - wrapper around dict"""

    def __init__(self, dict=None, **kwargs):
        self.data = {}
        if dict is not None:
            self.data.update(dict)
        if kwargs:
            self.data.update(kwargs)

    def __repr__(self):
        return repr(self.data)

    def __getitem__(self, key):
        return self.data[key]

    def __setitem__(self, key, value):
        self.data[key] = value


# UserList - Wrapper around list objects for easier list subclassing
class UserList:
    """Simplified stub for UserList - wrapper around list"""

    def __init__(self, initlist=None):
        self.data = []
        if initlist is not None:
            self.data = list(initlist)

    def __repr__(self):
        return repr(self.data)

    def __getitem__(self, index):
        return self.data[index]

    def __setitem__(self, index, value):
        self.data[index] = value

    def append(self, item):
        self.data.append(item)


# UserString - Wrapper around string objects for easier string subclassing
class UserString:
    """Simplified stub for UserString - wrapper around str"""

    def __init__(self, seq=''):
        self.data = str(seq)

    def __repr__(self):
        return repr(self.data)

    def __str__(self):
        return self.data
