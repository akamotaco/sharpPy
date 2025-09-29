"""Abstract Base Classes (ABCs) according to PEP 3119."""

def abstractmethod(funcobj):
    """A decorator indicating abstract methods."""
    funcobj.__isabstractmethod__ = True
    return funcobj

class ABCMeta(type):
    """Metaclass for defining Abstract Base Classes (ABCs)."""

    def __new__(mcls, name, bases, namespace):
        cls = super().__new__(mcls, name, bases, namespace)
        cls._abc_registry = set()
        cls._abc_cache = set()
        return cls

    def register(cls, subclass):
        """Register a virtual subclass of an ABC."""
        if not isinstance(subclass, type):
            raise TypeError("Can only register classes")
        cls._abc_registry.add(subclass)
        cls._abc_cache.add(subclass)
        return subclass

class ABC(metaclass=ABCMeta):
    """Helper class that provides a standard way to create an ABC using inheritance."""
    pass