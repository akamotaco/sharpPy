# collections.abc stub - Abstract Base Classes for collections
# CPython 3.12 compatible

class Iterable:
    pass

class Iterator(Iterable):
    pass

class Container:
    pass

class Sized:
    pass

class Callable:
    pass

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
