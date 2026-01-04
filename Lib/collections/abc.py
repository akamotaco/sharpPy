# collections.abc stub - Abstract Base Classes for collections
# CPython 3.12 compatible

class Hashable:
    """Hashable ABC - objects with __hash__"""
    __slots__ = ()

    def __hash__(self):
        return 0

class Awaitable:
    """Awaitable ABC - objects with __await__"""
    __slots__ = ()

class Coroutine(Awaitable):
    """Coroutine ABC"""
    __slots__ = ()

class AsyncIterable:
    """AsyncIterable ABC"""
    __slots__ = ()

class AsyncIterator(AsyncIterable):
    """AsyncIterator ABC"""
    __slots__ = ()

class AsyncGenerator(AsyncIterator):
    """AsyncGenerator ABC"""
    __slots__ = ()

class Iterable:
    pass

class Iterator(Iterable):
    pass

class Generator(Iterator):
    """Generator ABC"""
    __slots__ = ()

class Reversible(Iterable):
    """Reversible ABC"""
    __slots__ = ()

class Container:
    pass

class Sized:
    pass

class Callable:
    pass

class Collection(Sized, Iterable, Container):
    """Collection ABC"""
    __slots__ = ()

class Sequence(Sized, Iterable, Container):
    pass

class MutableSequence(Sequence):
    pass

class Set(Sized, Iterable, Container):
    pass

class MutableSet(Set):
    pass

class Mapping(Sized, Iterable, Container):
    pass

class MutableMapping(Mapping):
    pass

class MappingView(Sized):
    """MappingView ABC"""
    __slots__ = ()

class KeysView(MappingView, Set):
    """KeysView ABC"""
    __slots__ = ()

class ItemsView(MappingView, Set):
    """ItemsView ABC"""
    __slots__ = ()

class ValuesView(MappingView):
    """ValuesView ABC"""
    __slots__ = ()

class ByteString(Sequence):
    """ByteString ABC - deprecated in Python 3.12"""
    __slots__ = ()

class Buffer:
    """Buffer ABC"""
    __slots__ = ()
