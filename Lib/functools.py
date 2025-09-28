"""functools.py - Higher-order functions and operations on callable objects

CPython 3.12 정확한 구조:
- _functools C extension에서 performance-critical 함수들 import
- 추가 기능들은 pure Python으로 구현
"""

# Import C extension functions (CPython 3.12와 동일한 패턴)
try:
    from _functools import reduce
except ImportError:
    # Pure Python fallback for reduce
    def reduce(function, iterable, initial=None):
        """
        reduce(function, iterable[, initial]) -> value

        Apply a function of two arguments cumulatively to the items of an iterable,
        from left to right, so as to reduce the iterable to a single value.
        """
        it = iter(iterable)
        if initial is None:
            try:
                value = next(it)
            except StopIteration:
                raise TypeError("reduce() of empty sequence with no initial value") from None
        else:
            value = initial

        for element in it:
            value = function(value, element)
        return value

try:
    from _functools import partial
except ImportError:
    # Pure Python fallback for partial
    class partial:
        """
        partial(func, *args, **keywords) - new function with partial application
        of the given arguments and keywords.
        """
        __slots__ = "func", "args", "keywords", "__dict__", "__weakref__"

        def __init__(self, func, /, *args, **keywords):
            if not callable(func):
                raise TypeError("the first argument must be callable")
            self.func = func
            self.args = args
            self.keywords = keywords

        def __call__(self, /, *args, **keywords):
            keywords = {**self.keywords, **keywords}
            return self.func(*self.args, *args, **keywords)

        def __repr__(self):
            args = [repr(self.func)]
            args.extend(repr(x) for x in self.args)
            args.extend(f"{k}={v!r}" for k, v in self.keywords.items())
            return f"{self.__class__.__module__}.{self.__class__.__qualname__}({', '.join(args)})"

try:
    from _functools import cmp_to_key
except ImportError:
    # Pure Python fallback for cmp_to_key
    def cmp_to_key(mycmp):
        """Convert a cmp= function into a key= function"""
        class KeyWrapper(object):
            __slots__ = ['obj']
            def __init__(self, obj):
                self.obj = obj
            def __lt__(self, other):
                return mycmp(self.obj, other.obj) < 0
            def __gt__(self, other):
                return mycmp(self.obj, other.obj) > 0
            def __eq__(self, other):
                return mycmp(self.obj, other.obj) == 0
            def __le__(self, other):
                return mycmp(self.obj, other.obj) <= 0
            def __ge__(self, other):
                return mycmp(self.obj, other.obj) >= 0
            def __ne__(self, other):
                return mycmp(self.obj, other.obj) != 0
            def __hash__(self):
                return hash(self.obj)
        return KeyWrapper

# Metadata constants
WRAPPER_ASSIGNMENTS = ('__module__', '__name__', '__qualname__',
                       '__doc__', '__annotations__')
WRAPPER_UPDATES = ('__dict__',)

def update_wrapper(wrapper, wrapped,
                   assigned=WRAPPER_ASSIGNMENTS,
                   updated=WRAPPER_UPDATES):
    """Update a wrapper function to look like the wrapped function

    wrapper is the function to be updated
    wrapped is the original function
    assigned is a tuple naming the attributes assigned directly
    from the wrapped function to the wrapper function (defaults to
    WRAPPER_ASSIGNMENTS)
    updated is a tuple naming the attributes of the wrapper that
    are updated with the corresponding attribute from the wrapped
    function (defaults to WRAPPER_UPDATES)
    """
    # Copy attributes from wrapped to wrapper
    for attr in assigned:
        try:
            value = getattr(wrapped, attr)
        except AttributeError:
            pass
        else:
            setattr(wrapper, attr, value)

    # Update specified attributes
    for attr in updated:
        getattr(wrapper, attr).update(getattr(wrapped, attr, {}))

    # Set __wrapped__ to the original function
    wrapper.__wrapped__ = wrapped
    return wrapper

def wraps(wrapped,
          assigned=WRAPPER_ASSIGNMENTS,
          updated=WRAPPER_UPDATES):
    """Decorator factory to apply update_wrapper() to a wrapper function"""
    return lambda wrapper: update_wrapper(wrapper, wrapped, assigned, updated)

def total_ordering(cls):
    """Class decorator that fills in missing ordering methods"""
    # Define all comparison methods based on existing ones
    convert = {
        '__lt__': [('__gt__', lambda self, other: not (self < other or self == other)),
                   ('__le__', lambda self, other: self < other or self == other),
                   ('__ge__', lambda self, other: not self < other)],
        '__le__': [('__ge__', lambda self, other: not self <= other or self == other),
                   ('__lt__', lambda self, other: self <= other and not self == other),
                   ('__gt__', lambda self, other: not self <= other)],
        '__gt__': [('__lt__', lambda self, other: not (self > other or self == other)),
                   ('__ge__', lambda self, other: self > other or self == other),
                   ('__le__', lambda self, other: not self > other)],
        '__ge__': [('__le__', lambda self, other: not self >= other or self == other),
                   ('__gt__', lambda self, other: self >= other and not self == other),
                   ('__lt__', lambda self, other: not self >= other)]
    }

    # Find the base comparison method
    roots = [op for op in convert if hasattr(cls, op)]
    if not roots:
        raise ValueError('must define at least one ordering operation: < > <= >=')
    root = max(roots)  # prefer __lt__ to __le__ to __gt__ to __ge__

    # Generate the missing methods
    for opname, opfunc in convert[root]:
        if opname not in roots:
            opfunc.__name__ = opname
            setattr(cls, opname, opfunc)
    return cls

def cache(user_function):
    """Simple unbounded cache decorator"""
    return lru_cache(maxsize=None)(user_function)

def lru_cache(maxsize=128, typed=False):
    """Least-recently-used cache decorator"""
    # Simple implementation - CPython uses C extension optimization
    def decorating_function(user_function):
        cache_dict = {}
        cache_order = []
        hits = 0
        misses = 0

        def wrapper(*args, **kwds):
            nonlocal hits, misses

            # Create key from args and kwargs
            key = args
            if kwds:
                key = args + tuple(sorted(kwds.items()))
            if typed:
                key = key + tuple(type(v) for v in args)
                if kwds:
                    key = key + tuple(type(v) for v in kwds.values())

            # Check cache
            if key in cache_dict:
                hits += 1
                # Move to end (most recently used)
                cache_order.remove(key)
                cache_order.append(key)
                return cache_dict[key]

            # Not in cache, compute result
            misses += 1
            result = user_function(*args, **kwds)

            # Add to cache
            cache_dict[key] = result
            cache_order.append(key)

            # Evict if over maxsize
            if maxsize is not None and len(cache_dict) > maxsize:
                oldest = cache_order.pop(0)
                del cache_dict[oldest]

            return result

        def cache_info():
            """Report cache statistics"""
            return {"hits": hits, "misses": misses, "maxsize": maxsize, "currsize": len(cache_dict)}

        def cache_clear():
            """Clear the cache and statistics"""
            nonlocal hits, misses
            cache_dict.clear()
            cache_order.clear()
            hits = misses = 0

        wrapper.cache_info = cache_info
        wrapper.cache_clear = cache_clear
        return update_wrapper(wrapper, user_function)

    return decorating_function

def singledispatch(func):
    """Single-dispatch generic function decorator"""
    registry = {}
    dispatch_cache = {}

    def dispatch(typ):
        """Return the implementation for the given type"""
        try:
            return dispatch_cache[typ]
        except KeyError:
            pass

        try:
            impl = registry[typ]
        except KeyError:
            impl = _find_impl(typ, registry)

        dispatch_cache[typ] = impl
        return impl

    def _find_impl(cls, registry):
        """Find implementation for class in registry via MRO"""
        mro = getattr(cls, '__mro__', None)
        if mro is None:
            return registry.get(object, func)

        for base in mro:
            if base in registry:
                return registry[base]

        return func

    def register(typ, func_impl=None):
        """Register an implementation for a type"""
        if func_impl is None:
            return lambda f: register(typ, f)

        registry[typ] = func_impl
        dispatch_cache.clear()
        return func_impl

    def wrapper(*args, **kw):
        if not args:
            raise TypeError('singledispatch requires at least one argument')
        return dispatch(type(args[0]))(*args, **kw)

    wrapper.register = register
    wrapper.dispatch = dispatch
    wrapper.registry = registry.copy()

    update_wrapper(wrapper, func)
    return wrapper

class singledispatchmethod:
    """Single-dispatch generic method descriptor"""

    def __init__(self, func):
        if not callable(func) and not hasattr(func, "__get__"):
            raise TypeError(f"{func!r} is not callable or a descriptor")

        self.dispatcher = singledispatch(func)
        self.func = func

    def register(self, cls, method=None):
        """Register a new implementation for the given type"""
        return self.dispatcher.register(cls, method)

    def __get__(self, obj, cls=None):
        def _method(*args, **kwargs):
            method = self.dispatcher.dispatch(args[0].__class__)
            return method.__get__(obj, cls)(*args, **kwargs)
        _method.__isabstractmethod__ = self.__isabstractmethod__
        _method.register = self.register
        update_wrapper(_method, self.func)
        return _method

    @property
    def __isabstractmethod__(self):
        return getattr(self.func, '__isabstractmethod__', False)

class cached_property:
    """Cached property decorator"""

    def __init__(self, func):
        self.func = func
        self.attrname = None
        self.__doc__ = func.__doc__

    def __set_name__(self, owner, name):
        if self.attrname is None:
            self.attrname = name
        elif name != self.attrname:
            raise RuntimeError(
                f"Cannot assign the same cached_property to two different names "
                f"({self.attrname!r} and {name!r})."
            )

    def __get__(self, instance, owner=None):
        if instance is None:
            return self
        if self.attrname is None:
            raise TypeError(
                "Cannot use cached_property instance without calling __set_name__ on it.")
        try:
            cache = instance.__dict__
        except AttributeError:
            msg = (
                f"No '__dict__' attribute on {type(instance).__name__!r} "
                f"instance to cache {self.attrname!r} property."
            )
            raise TypeError(msg) from None
        val = cache.get(self.attrname, _NOT_FOUND)
        if val is _NOT_FOUND:
            val = self.func(instance)
            try:
                cache[self.attrname] = val
            except TypeError:
                msg = (
                    f"The '__dict__' attribute on {type(instance).__name__!r} instance "
                    f"does not support item assignment for caching {self.attrname!r} property."
                )
                raise TypeError(msg) from None
        return val

    def __delete__(self, instance):
        if self.attrname is None:
            raise TypeError(
                "Cannot use cached_property instance without calling __set_name__ on it.")
        try:
            cache = instance.__dict__
        except AttributeError:
            msg = (
                f"No '__dict__' attribute on {type(instance).__name__!r} "
                f"instance to delete {self.attrname!r}."
            )
            raise TypeError(msg) from None
        try:
            del cache[self.attrname]
        except KeyError:
            pass

_NOT_FOUND = object()

# Export public API - CPython 3.12와 동일
__all__ = [
    'update_wrapper', 'wraps', 'WRAPPER_ASSIGNMENTS', 'WRAPPER_UPDATES',
    'total_ordering', 'cache', 'cmp_to_key', 'lru_cache', 'reduce', 'partial',
    'partialmethod', 'singledispatch', 'singledispatchmethod', 'cached_property'
]

# Note: partialmethod은 method descriptor 지원이 필요하여 현재 구현하지 않음