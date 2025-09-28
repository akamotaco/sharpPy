"""Support for type hints - Simple implementation."""


class _GenericAlias:
    """The central part of internal API for type aliases."""
    pass


class _SpecialForm:
    """A special form in the type system."""
    pass


class Any:
    """Special type indicating an unconstrained type."""
    pass


class Union:
    """Union type; Union[X, Y] means either X or Y."""
    pass


class Optional:
    """Optional type."""
    pass


class List:
    """Generic version of list."""
    pass


class Dict:
    """Generic version of dict."""
    pass


class Tuple:
    """Generic version of tuple."""
    pass


class Set:
    """Generic version of set."""
    pass


class FrozenSet:
    """Generic version of frozenset."""
    pass


class Callable:
    """Callable type for function annotations."""
    pass


class Type:
    """A special construct usable to annotate class objects."""
    pass


def get_type_hints(obj, globalns=None, localns=None):
    """Return type hints for a callable object."""
    return {}


def cast(typ, val):
    """Cast a value to a type."""
    return val


def overload(func):
    """Decorator for marking overloaded functions."""
    return func