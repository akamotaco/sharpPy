"""
Collections module - Specialized container datatypes

This module provides Python implementations of the core collections types:
- deque: double-ended queue
- Counter: dict for counting hashable objects
- defaultdict: dict with default values
- OrderedDict: dict that remembers insertion order
- ChainMap: dict-like class for creating a single view of multiple mappings
- namedtuple: factory function for creating tuple subclasses with named fields
"""

from typing import Any, Dict, List, Optional, Tuple, Union, Callable, Iterator


class deque:
    """Double-ended queue."""

    def __init__(self, iterable=None, maxlen=None):
        self._items = []
        self.maxlen = maxlen

        if iterable is not None:
            for item in iterable:
                self.append(item)

    def append(self, item):
        """Add an element to the right side of the deque."""
        self._items.append(item)
        if self.maxlen is not None and len(self._items) > self.maxlen:
            self.popleft()

    def appendleft(self, item):
        """Add an element to the left side of the deque."""
        self._items.insert(0, item)
        if self.maxlen is not None and len(self._items) > self.maxlen:
            self.pop()

    def pop(self):
        """Remove and return an element from the right side of the deque."""
        if not self._items:
            raise IndexError("pop from empty deque")
        return self._items.pop()

    def popleft(self):
        """Remove and return an element from the left side of the deque."""
        if not self._items:
            raise IndexError("pop from empty deque")
        return self._items.pop(0)

    def clear(self):
        """Remove all elements from the deque."""
        self._items.clear()

    def extend(self, iterable):
        """Extend the right side of the deque by appending elements from iterable."""
        for item in iterable:
            self.append(item)

    def extendleft(self, iterable):
        """Extend the left side of the deque by appending elements from iterable."""
        for item in iterable:
            self.appendleft(item)

    def rotate(self, n=1):
        """Rotate the deque n steps to the right (or left if negative)."""
        if not self._items:
            return
        n = n % len(self._items)
        if n:
            self._items = self._items[-n:] + self._items[:-n]

    def __len__(self):
        return len(self._items)

    def __iter__(self):
        return iter(self._items)

    def __repr__(self):
        if self.maxlen is not None:
            return f"deque({self._items!r}, maxlen={self.maxlen})"
        return f"deque({self._items!r})"


class Counter(dict):
    """Dict subclass for counting hashable objects."""

    def __init__(self, iterable=None, **kwds):
        super().__init__()
        self.update(iterable, **kwds)

    def update(self, iterable=None, **kwds):
        """Like dict.update() but adds counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) + count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) + 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) + count

    def most_common(self, n=None):
        """Return a list of the n most common elements and their counts."""
        if n is None:
            return sorted(self.items(), key=lambda x: x[1], reverse=True)
        return sorted(self.items(), key=lambda x: x[1], reverse=True)[:n]

    def subtract(self, iterable=None, **kwds):
        """Subtract counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) - count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) - 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) - count

    def __missing__(self, key):
        return 0

    def __repr__(self):
        if not self:
            return f'{self.__class__.__name__}()'
        items = ', '.join(f'{k!r}: {v!r}' for k, v in self.most_common())
        return f'{self.__class__.__name__}({{{items}}})'


class defaultdict(dict):
    """Dict subclass that calls a factory function to supply missing values."""

    def __init__(self, default_factory=None, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.default_factory = default_factory

    def __getitem__(self, key):
        try:
            return super().__getitem__(key)
        except KeyError:
            return self.__missing__(key)

    def __missing__(self, key):
        if self.default_factory is None:
            raise KeyError(key)
        self[key] = value = self.default_factory()
        return value

    def __repr__(self):
        return f"defaultdict({self.default_factory!r}, {super().__repr__()})"


class OrderedDict(dict):
    """Dict that remembers insertion order (Python 3.7+ behavior)."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)

    def move_to_end(self, key, last=True):
        """Move an existing element to the end (or beginning if last is false)."""
        if key not in self:
            raise KeyError(key)
        value = self.pop(key)
        if last:
            self[key] = value
        else:
            items = list(self.items())
            self.clear()
            self[key] = value
            self.update(items)

    def popitem(self, last=True):
        """Remove and return a (key, value) pair."""
        if not self:
            raise KeyError('dictionary is empty')
        if last:
            return super().popitem()
        else:
            key = next(iter(self))
            value = self[key]
            del self[key]
            return key, value


class ChainMap(dict):
    """A ChainMap groups multiple dicts or other mappings together."""

    def __init__(self, *maps):
        self.maps = list(maps) or [{}]

    def __getitem__(self, key):
        for mapping in self.maps:
            try:
                return mapping[key]
            except KeyError:
                pass
        raise KeyError(key)

    def __setitem__(self, key, value):
        self.maps[0][key] = value

    def __delitem__(self, key):
        try:
            del self.maps[0][key]
        except KeyError:
            raise KeyError(f"Key not found in the first mapping: {key!r}")

    def __iter__(self):
        seen = set()
        for mapping in self.maps:
            for key in mapping:
                if key not in seen:
                    seen.add(key)
                    yield key

    def __len__(self):
        return len(set().union(*self.maps))

    def __repr__(self):
        return f"{self.__class__.__name__}({', '.join(map(repr, self.maps))})"

    def new_child(self, m=None):
        """Create a new ChainMap with a new map followed by all previous maps."""
        if m is None:
            m = {}
        return self.__class__(m, *self.maps)

    @property
    def parents(self):
        """Return a new ChainMap containing all maps except the first."""
        return self.__class__(*self.maps[1:])


def namedtuple(typename, field_names, *, rename=False, defaults=None, module=None):
    """Factory function for creating tuple subclasses with named fields."""

    # Parse field names
    if isinstance(field_names, str):
        field_names = field_names.replace(',', ' ').split()
    field_names = list(field_names)

    if rename:
        # Replace invalid names
        for i, name in enumerate(field_names):
            if not name.isidentifier() or name.startswith('_'):
                field_names[i] = f'_{i}'

    # Validate field names
    seen = set()
    for name in field_names:
        if not name.isidentifier():
            raise ValueError(f'Field names must be valid identifiers: {name!r}')
        if name.startswith('_'):
            raise ValueError(f'Field names cannot start with an underscore: {name!r}')
        if name in seen:
            raise ValueError(f'Encountered duplicate field name: {name!r}')
        seen.add(name)

    # Create the class
    class_definition = f'''
class {typename}(tuple):
    __slots__ = ()
    _fields = {field_names!r}

    def __new__(cls, {', '.join(field_names)}):
        return tuple.__new__(cls, ({', '.join(field_names)}))

    @classmethod
    def _make(cls, iterable):
        return cls(*iterable)

    def _asdict(self):
        return dict(zip(self._fields, self))

    def _replace(self, **kwds):
        result = self._asdict()
        result.update(kwds)
        return self.__class__(**result)

    def __repr__(self):
        return f'{typename}({", ".join(f"{f}={v!r}" for f, v in zip(self._fields, self))})'

    def __getnewargs__(self):
        return tuple(self)
'''

    # Add property accessors for each field
    for i, field_name in enumerate(field_names):
        class_definition += f'''
    @property
    def {field_name}(self):
        return self[{i}]
'''

    # Execute the class definition
    namespace = {}
    exec(class_definition, namespace)
    namedtuple_class = namespace[typename]

    # Set module if provided
    if module is not None:
        namedtuple_class.__module__ = module

    # Handle defaults
    if defaults is not None:
        namedtuple_class.__new__.__defaults__ = tuple(defaults)

    return namedtuple_class


# Export public API
__all__ = [
    'deque', 'Counter', 'defaultdict', 'OrderedDict', 'ChainMap', 'namedtuple'
]