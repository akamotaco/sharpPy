# abc.py - Abstract Base Classes
# CPython 3.12 compatible simplified version for SharpPy
# Reference: CPython Lib/abc.py

from _abc import (
    get_cache_token,
    _abc_init,
    _abc_instancecheck,
    _abc_subclasscheck,
    _abc_register,
    _reset_registry,
    _reset_caches,
    _get_dump,
)

def abstractmethod(funcobj):
    """A decorator indicating abstract methods."""
    funcobj.__isabstractmethod__ = True
    return funcobj

class abstractclassmethod(classmethod):
    """A decorator indicating abstract classmethods."""
    __isabstractmethod__ = True

    def __init__(self, callable):
        callable.__isabstractmethod__ = True
        super().__init__(callable)

class abstractstaticmethod(staticmethod):
    """A decorator indicating abstract staticmethods."""
    __isabstractmethod__ = True

    def __init__(self, callable):
        callable.__isabstractmethod__ = True
        super().__init__(callable)

class abstractproperty(property):
    """A decorator indicating abstract properties."""
    __isabstractmethod__ = True

def update_abstractmethods(cls):
    """Recalculate the set of abstract methods of an abstract class."""
    if not hasattr(cls, '__abstractmethods__'):
        return cls

    abstracts = set()
    for name in dir(cls):
        value = getattr(cls, name, None)
        if getattr(value, '__isabstractmethod__', False):
            abstracts.add(name)

    cls.__abstractmethods__ = frozenset(abstracts)
    return cls

class ABCMeta(type):
    """Metaclass for defining Abstract Base Classes (ABCs)."""

    def __new__(mcls, name, bases, namespace, **kwargs):
        cls = super().__new__(mcls, name, bases, namespace, **kwargs)
        _abc_init(cls)
        return cls

    def register(cls, subclass):
        """Register a virtual subclass of an ABC."""
        return _abc_register(cls, subclass)

    def __instancecheck__(cls, instance):
        """Override for isinstance(instance, cls)."""
        return _abc_instancecheck(cls, instance)

    def __subclasscheck__(cls, subclass):
        """Override for issubclass(subclass, cls)."""
        return _abc_subclasscheck(cls, subclass)

class ABC(metaclass=ABCMeta):
    """Helper class that provides a standard way to create an ABC using
    inheritance.
    """
    __slots__ = ()
