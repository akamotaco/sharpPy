"""collections - Specialized container datatypes

CPython 3.12 compatible implementation for SharpPy.
Provides namedtuple and other container types.
"""

from keyword import iskeyword as _iskeyword
from operator import itemgetter as _itemgetter
import sys as _sys

__all__ = ['namedtuple', 'deque', 'defaultdict', 'OrderedDict', 'Counter']

# CPython 3.12: _tuplegetter for creating field properties
# collections/__init__.py line 350-353
try:
    from _collections import _tuplegetter
except (ImportError, AttributeError):
    # SharpPy: AttributeError is raised when _collections exists but doesn't have _tuplegetter
    _tuplegetter = lambda index, doc: property(_itemgetter(index), doc=doc)

# CPython 3.12: namedtuple implementation
# Based on Lib/collections/__init__.py
def namedtuple(typename, field_names, *, rename=False, defaults=None, module=None):
    """Returns a new subclass of tuple with named fields.

    >>> Point = namedtuple('Point', ['x', 'y'])
    >>> Point.__doc__                   # docstring for the new class
    'Point(x, y)'
    >>> p = Point(11, y=22)             # instantiate with positional args or keywords
    >>> p[0] + p[1]                     # indexable like a plain tuple
    33
    >>> x, y = p                        # unpack like a regular tuple
    >>> x, y
    (11, 22)
    >>> p.x + p.y                       # fields also accessible by name
    33
    >>> d = p._asdict()                 # convert to a dictionary
    >>> d['x']
    11
    >>> Point(**d)                      # convert from a dictionary
    Point(x=11, y=22)
    >>> p._replace(x=100)               # _replace() is like str.replace() but targets named fields
    Point(x=100, y=22)
    """

    # Parse field_names
    if isinstance(field_names, str):
        field_names = field_names.replace(',', ' ').split()
    field_names = list(map(str, field_names))

    # Validate typename
    if not typename.isidentifier():
        raise ValueError(f'Type name must be a valid identifier: {typename!r}')
    if _iskeyword(typename):
        raise ValueError(f'Type name cannot be a keyword: {typename!r}')

    # Validate field names
    seen = set()
    for name in field_names:
        if not name.isidentifier() and not rename:
            raise ValueError(f'Field name must be a valid identifier: {name!r}')
        if _iskeyword(name) and not rename:
            raise ValueError(f'Field name cannot be a keyword: {name!r}')
        if name.startswith('_') and not rename:
            raise ValueError(f'Field name cannot start with an underscore: {name!r}')
        if name in seen:
            raise ValueError(f'Encountered duplicate field name: {name!r}')
        seen.add(name)

    # Rename invalid field names if requested
    if rename:
        names = list(field_names)
        seen = set()
        for index, name in enumerate(names):
            if (not name.isidentifier()
                or _iskeyword(name)
                or name.startswith('_')
                or name in seen):
                names[index] = f'_{index}'
            seen.add(names[index])
        field_names = names

    # Create the tuple subclass
    field_names_str = ', '.join(field_names)
    arg_list = ', '.join(field_names)
    # CPython 3.12: repr_fmt includes parentheses for use with % formatting
    # Use list comprehension instead of generator expression to avoid compilation issues
    repr_fmt = '(' + ', '.join([f'{name}=%r' for name in field_names]) + ')'

    # CPython uses exec to create the class dynamically
    # We'll use a simpler approach with type()

    # Create __new__ method
    def __new__(cls, *args, **kwargs):
        'Create new instance of {typename}({field_names_str})'
        # Build the tuple from positional and keyword arguments
        if len(args) > len(field_names):
            raise TypeError(f'{typename} takes at most {len(field_names)} arguments ({len(args)} given)')

        # Start with positional args
        result_list = list(args)

        # Add keyword args
        for i, name in enumerate(field_names[len(args):], start=len(args)):
            if name in kwargs:
                result_list.append(kwargs.pop(name))
            elif defaults and i >= len(field_names) - len(defaults):
                # Use default value
                default_index = i - (len(field_names) - len(defaults))
                result_list.append(defaults[default_index])
            else:
                raise TypeError(f'{typename}() missing required argument: {name!r}')

        # Check for unexpected keyword arguments
        if kwargs:
            raise TypeError(f'{typename}() got unexpected keyword argument: {next(iter(kwargs))!r}')

        return tuple.__new__(cls, result_list)

    # Create __repr__ method
    # CPython 3.12: Use pre-computed repr_fmt with % formatting instead of generator expression
    # This avoids nested function + f-string + generator expression compilation issues
    def __repr__(self):
        'Return a nicely formatted representation string'
        return self.__class__.__name__ + repr_fmt % self

    # Create _asdict method
    def _asdict(self):
        'Return a new dict which maps field names to their values.'
        return dict(zip(field_names, self))

    # Create _replace method
    def _replace(self, **kwds):
        'Return a new {typename} object replacing specified fields with new values'
        result = dict(zip(field_names, self))
        result.update(kwds)
        return type(self)(**result)

    # Create _make classmethod
    @classmethod
    def _make(cls, iterable):
        'Make a new {typename} object from a sequence or iterable'
        return cls(*iterable)

    # Create the class
    namespace = {
        '__new__': __new__,
        '__repr__': __repr__,
        '_asdict': _asdict,
        '_replace': _replace,
        '_make': _make,
        '__slots__': (),
        '_fields': tuple(field_names),
        '__module__': module or 'collections',
    }

    # Add properties for each field using _tuplegetter
    # CPython 3.12: collections/__init__.py line 504-506
    for index, name in enumerate(field_names):
        doc = f'Alias for field number {index}'
        namespace[name] = _tuplegetter(index, doc)

    # Create the class using type()
    result = type(typename, (tuple,), namespace)

    return result


# deque implementation
# CPython 3.12: Modules/_collectionsmodule.c (deque_type)
class deque(list):
    """Double-ended queue implementation"""
    def __init__(self, iterable=None, maxlen=None):
        super().__init__()
        self.maxlen = maxlen
        if iterable is not None:
            for elem in iterable:
                self.append(elem)

    def appendleft(self, x):
        """Add an element to the left side of the deque."""
        self.insert(0, x)
        if self.maxlen is not None and len(self) > self.maxlen:
            self.pop()

    def popleft(self):
        """Remove and return an element from the left side of the deque."""
        if not self:
            raise IndexError("pop from an empty deque")
        return super().pop(0)

    def extendleft(self, iterable):
        """Extend the left side of the deque with elements from iterable."""
        for elem in iterable:
            self.appendleft(elem)

    def rotate(self, n=1):
        """Rotate the deque n steps to the right (negative for left)."""
        if not self:
            return
        n = n % len(self)
        if n:
            # Move last n elements to front (in order)
            # Pop from right: [0,1,2,3].rotate(2) -> pop 3, pop 2 -> temp=[3,2]
            # Insert at front in reverse: insert 2 then 3 -> [2,3,0,1]
            temp = []
            for _ in range(n):
                temp.append(super(deque, self).pop())
            # Reverse to maintain original order
            temp.reverse()
            # Insert all at front (in order)
            for i, elem in enumerate(temp):
                self.insert(i, elem)

class defaultdict(dict):
    """defaultdict stub - inherits from dict"""
    def __init__(self, default_factory=None, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.default_factory = default_factory

    def __missing__(self, key):
        if self.default_factory is None:
            raise KeyError(key)
        self[key] = self.default_factory()
        return self[key]

class OrderedDict(dict):
    """OrderedDict - Python 3.7+ dicts are ordered by default"""
    # CPython 3.12: Lib/collections/__init__.py line 80-176

    def __init__(self, other=None, **kwds):
        """Initialize an ordered dictionary."""
        super().__init__()
        if other is not None:
            if hasattr(other, 'items'):
                for key, value in other.items():
                    self[key] = value
            elif hasattr(other, '__iter__'):
                for key, value in other:
                    self[key] = value
        for key, value in kwds.items():
            self[key] = value

    def move_to_end(self, key, last=True):
        """Move an existing element to either end of the dictionary.

        The element is moved to the right end if last is True (default)
        or to the beginning if last is False.
        """
        if key not in self:
            raise KeyError(key)
        value = self[key]
        if last:
            # Delete and re-add to move to end
            # Use dict.__delitem__ directly to avoid issues with super() binding
            dict.__delitem__(self, key)
            self[key] = value
        else:
            # For last=False, we need to rebuild the entire dict
            # Save all items except the target key
            items = [(k, v) for k, v in self.items() if k != key]
            # Clear by deleting all keys one by one
            # (dict.clear has descriptor issues with dict subclasses in SharpPy)
            for k in list(self.keys()):
                dict.__delitem__(self, k)
            # Rebuild with target key first
            self[key] = value
            for k, v in items:
                self[k] = v

    def popitem(self, last=True):
        """Remove and return a (key, value) pair from the dictionary.

        Pairs are returned in LIFO order if last is True or FIFO order if False.
        """
        if not self:
            raise KeyError('dictionary is empty')
        if last:
            key = list(self.keys())[-1]
        else:
            key = list(self.keys())[0]
        value = self.pop(key)
        return (key, value)

class Counter(dict):
    """Dict subclass for counting hashable items.
    CPython 3.12: Lib/collections/__init__.py line 541-759
    """

    def __init__(self, iterable=None, **kwds):
        super().__init__()
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) + count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) + 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) + count

    def __missing__(self, key):
        return 0

    def most_common(self, n=None):
        """List the n most common elements and their counts from the most
        common to the least.  If n is None, then list all element counts.

        >>> Counter('abracadabra').most_common(3)
        [('a', 5), ('b', 2), ('r', 2)]
        """
        if n is None:
            return sorted(self.items(), key=lambda item: item[1], reverse=True)
        # Use sorted instead of heapq for simplicity
        return sorted(self.items(), key=lambda item: item[1], reverse=True)[:n]

    def elements(self):
        """Iterator over elements repeating each as many times as its count.

        >>> c = Counter('ABCABC')
        >>> sorted(c.elements())
        ['A', 'A', 'B', 'B', 'C', 'C']

        Positive counts only (ignores zero and negative counts).
        """
        for elem, count in self.items():
            for _ in range(count):
                yield elem

    def update(self, iterable=None, **kwds):
        """Like dict.update() but add counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) + count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) + 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) + count

    def subtract(self, iterable=None, **kwds):
        """Like dict.update() but subtracts counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) - count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) - 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) - count

    def total(self):
        """Sum of all counts."""
        return sum(self.values())
