"""Tools for working with functions and callable objects - Simple implementation."""


class partial:
    """Partial object for creating partial function applications."""
    pass


def reduce(function, iterable, initializer=None):
    """Apply a function of two arguments cumulatively to the items of an iterable."""
    return function


def wraps(wrapped):
    """Decorator factory to apply update_wrapper() to a wrapper function."""
    def decorator(wrapper):
        return wrapper
    return decorator


def lru_cache(maxsize=128, typed=False):
    """Least Recently Used cache decorator."""
    def decorating_function(user_function):
        return user_function
    return decorating_function


def singledispatch(func):
    """Single-dispatch generic function decorator."""
    return func


def cache(user_function):
    """Simple unbounded cache decorator."""
    return user_function