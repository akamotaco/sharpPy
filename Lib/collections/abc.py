"""
Abstract Base Classes for collections

This module provides abstract base classes that can be used to test whether
a class provides a particular interface.
"""

# Basic abstract base classes that typing.py might need
class Iterable:
    """Abstract base class for iterable objects."""
    pass

class Iterator(Iterable):
    """Abstract base class for iterator objects."""
    pass

class Container:
    """Abstract base class for container objects."""
    pass

class Sized:
    """Abstract base class for objects with a size."""
    pass

class Callable:
    """Abstract base class for callable objects."""
    pass

class Sequence(Sized, Iterable, Container):
    """Abstract base class for sequence objects."""
    pass

class MutableSequence(Sequence):
    """Abstract base class for mutable sequence objects."""
    pass

class Set(Sized, Iterable, Container):
    """Abstract base class for set objects."""
    pass

class MutableSet(Set):
    """Abstract base class for mutable set objects."""
    pass

class Mapping(Sized, Iterable, Container):
    """Abstract base class for mapping objects."""
    pass

class MutableMapping(Mapping):
    """Abstract base class for mutable mapping objects."""
    pass

# Export common names
__all__ = [
    'Iterable', 'Iterator', 'Container', 'Sized', 'Callable',
    'Sequence', 'MutableSequence', 'Set', 'MutableSet',
    'Mapping', 'MutableMapping'
]