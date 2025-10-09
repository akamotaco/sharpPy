# Copyright 2007 Google, Inc. All Rights Reserved.
# Licensed to PSF under a Contributor Agreement.

"""Abstract Base Classes (ABCs) according to PEP 3119.

Full implementation for SharpPy compatible with CPython 3.12.
"""

# CPython 3.12: Global cache token counter
_abc_cache_token = 0

def get_cache_token():
    """Returns the current ABC cache token.

    The token is an opaque object (supporting equality testing) identifying the
    current version of the ABC cache for virtual subclasses. The token changes
    with every call to register() on any ABC.
    """
    return _abc_cache_token


def abstractmethod(funcobj):
    """A decorator indicating abstract methods.

    Requires that the metaclass is ABCMeta or derived from it.  A
    class that has a metaclass derived from ABCMeta cannot be
    instantiated unless all of its abstract methods are overridden.
    The abstract methods can be called using any of the normal
    'super' call mechanisms.  abstractmethod() may be used to declare
    abstract methods for properties and descriptors.

    Usage:

        class C(metaclass=ABCMeta):
            @abstractmethod
            def my_abstract_method(self, arg1, arg2, argN):
                ...
    """
    funcobj.__isabstractmethod__ = True
    return funcobj


class abstractclassmethod(classmethod):
    """A decorator indicating abstract classmethods.

    Deprecated, use 'classmethod' with 'abstractmethod' instead:

        class C(ABC):
            @classmethod
            @abstractmethod
            def my_abstract_classmethod(cls, ...):
                ...

    """

    __isabstractmethod__ = True

    def __init__(self, callable):
        callable.__isabstractmethod__ = True
        super().__init__(callable)


class abstractstaticmethod(staticmethod):
    """A decorator indicating abstract staticmethods.

    Deprecated, use 'staticmethod' with 'abstractmethod' instead:

        class C(ABC):
            @staticmethod
            @abstractmethod
            def my_abstract_staticmethod(...):
                ...

    """

    __isabstractmethod__ = True

    def __init__(self, callable):
        callable.__isabstractmethod__ = True
        super().__init__(callable)


class abstractproperty(property):
    """A decorator indicating abstract properties.

    Deprecated, use 'property' with 'abstractmethod' instead:

        class C(ABC):
            @property
            @abstractmethod
            def my_abstract_property(self):
                ...

    """

    __isabstractmethod__ = True


class ABCMeta(type):
    """Metaclass for defining Abstract Base Classes (ABCs).

    Use this metaclass to create an ABC.  An ABC can be subclassed
    directly, and then acts as a mix-in class.  You can also register
    unrelated concrete classes (even built-in classes) and unrelated
    ABCs as 'virtual subclasses' -- these and their descendants will
    be considered subclasses of the registering ABC by the built-in
    issubclass() function, but the registering ABC won't show up in
    their MRO (Method Resolution Order) nor will method
    implementations defined by the registering ABC be callable (not
    even via super()).
    """

    def __new__(mcls, name, bases, namespace, **kwargs):
        cls = super().__new__(mcls, name, bases, namespace, **kwargs)
        # Compute abstract methods (CPython 3.12 _abc_init logic)
        abstracts = set()

        # Collect inherited abstract methods from base classes
        for base in bases:
            base_abstracts = getattr(base, '__abstractmethods__', ())
            for name_item in base_abstracts:
                value = getattr(cls, name_item, None)
                if getattr(value, "__isabstractmethod__", False):
                    abstracts.add(name_item)

        # Add newly defined abstract methods
        for name_item, value in namespace.items():
            if getattr(value, "__isabstractmethod__", False):
                abstracts.add(name_item)

        cls.__abstractmethods__ = frozenset(abstracts)

        # Initialize registry and cache (CPython 3.12 _abc_data)
        cls._abc_registry = set()
        cls._abc_cache = set()
        cls._abc_negative_cache = set()
        cls._abc_negative_cache_version = 0

        return cls

    def register(cls, subclass):
        """Register a virtual subclass of an ABC.

        Returns the subclass, to allow usage as a class decorator.
        """
        global _abc_cache_token

        if not isinstance(subclass, type):
            raise TypeError("Can only register classes")

        # Prevent inheritance cycles
        if issubclass(cls, subclass):
            raise RuntimeError("Refusing to create an inheritance cycle")

        cls._abc_registry.add(subclass)
        cls._abc_cache.add(subclass)
        # Invalidate negative cache
        cls._abc_negative_cache.clear()
        cls._abc_negative_cache_version += 1

        # CPython 3.12: Increment global cache token
        _abc_cache_token += 1

        return subclass

    def __instancecheck__(cls, instance):
        """Override for isinstance(instance, cls)."""
        # CPython 3.12 _abc_instancecheck logic
        subclass = instance.__class__
        return cls.__subclasscheck__(subclass)

    def __subclasscheck__(cls, subclass):
        """Override for issubclass(subclass, cls)."""
        # CPython 3.12 _abc_subclasscheck logic

        # Fast path: check positive cache
        if subclass in cls._abc_cache:
            return True

        # Check negative cache
        if subclass in cls._abc_negative_cache:
            return False

        # Check if in registry
        if subclass in cls._abc_registry:
            cls._abc_cache.add(subclass)
            return True

        # Check through normal inheritance (MRO)
        if any(c is cls for c in subclass.__mro__):
            cls._abc_cache.add(subclass)
            return True

        # Check __subclasshook__ if it exists
        if hasattr(cls, '__subclasshook__'):
            hook_result = cls.__subclasshook__(subclass)
            if hook_result is True:
                cls._abc_cache.add(subclass)
                return True
            elif hook_result is False:
                cls._abc_negative_cache.add(subclass)
                return False
            # If NotImplemented, continue checking

        # Check registered subclasses recursively
        for registered in cls._abc_registry:
            if issubclass(subclass, registered):
                cls._abc_cache.add(subclass)
                return True

        # Not a subclass
        cls._abc_negative_cache.add(subclass)
        return False


def update_abstractmethods(cls):
    """Recalculate the set of abstract methods of an abstract class.

    If a class has had one of its abstract methods implemented after the
    class was created, the method will not be considered implemented until
    this function is called. Alternatively, if a new abstract method has been
    added to the class, it will only be considered an abstract method of the
    class after this function is called.

    This function should be called before any use is made of the class,
    usually in class decorators that add methods to the subject class.

    Returns cls, to allow usage as a class decorator.

    If cls is not an instance of ABCMeta, does nothing.
    """
    if not hasattr(cls, '__abstractmethods__'):
        # We check for __abstractmethods__ here because cls might by a C
        # implementation or a python implementation (especially during
        # testing), and we want to handle both cases.
        return cls

    abstracts = set()
    # Check the existing abstract methods of the parents, keep only the ones
    # that are not implemented.
    for scls in cls.__bases__:
        for name in getattr(scls, '__abstractmethods__', ()):
            value = getattr(cls, name, None)
            if getattr(value, "__isabstractmethod__", False):
                abstracts.add(name)
    # Also add any other newly added abstract methods.
    for name, value in cls.__dict__.items():
        if getattr(value, "__isabstractmethod__", False):
            abstracts.add(name)
    cls.__abstractmethods__ = frozenset(abstracts)
    return cls


class ABC(metaclass=ABCMeta):
    """Helper class that provides a standard way to create an ABC using
    inheritance.
    """
    __slots__ = ()
