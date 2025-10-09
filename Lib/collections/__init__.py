"""collections - Specialized container datatypes

CPython 3.12 compatible implementation for SharpPy.
Provides namedtuple and other container types.
"""

from keyword import iskeyword as _iskeyword
import sys as _sys

__all__ = ['namedtuple', 'deque', 'defaultdict', 'OrderedDict', 'Counter']

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
    repr_fmt = ', '.join(f'{name}=%r' for name in field_names)

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
    def __repr__(self):
        'Return a nicely formatted representation string'
        values = tuple(self)
        return f'{typename}({", ".join(f"{name}={val!r}" for name, val in zip(field_names, values))})'

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

    # Add properties for each field
    for index, name in enumerate(field_names):
        namespace[name] = property(lambda self, i=index: self[i])

    # Create the class using type()
    result = type(typename, (tuple,), namespace)

    return result


# Stub implementations for other types
class deque(list):
    """Deque stub - inherits from list for basic functionality"""
    pass

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
    """OrderedDict stub - Python 3.7+ dicts are ordered by default"""
    pass

class Counter(dict):
    """Counter stub - dict subclass for counting"""
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
