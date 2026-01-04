"""
_typing - C extension module stub for typing support (CPython 3.12 compatible)

This module provides Python implementations of the types that are normally
implemented in C in CPython's _typing module (Modules/_typingmodule.c).

SharpPy implements these as pure Python stubs to support CPython 3.12's typing.py.
"""

# CPython 3.12: Modules/_typingmodule.c

def _idfunc(x):
    """Identity function used by NewType.__call__.

    CPython 3.12: Modules/_typingmodule.c:28-33
    """
    return x


class TypeVar:
    """Type variable.

    CPython 3.12: Objects/typevarobject.c

    Usage::

        T = TypeVar('T')  # Can be anything
        S = TypeVar('S', bound=str)  # Can be any subtype of str
        A = TypeVar('A', str, bytes)  # Must be exactly str or bytes
    """

    def __init__(self, name, *constraints, bound=None,
                 covariant=False, contravariant=False, infer_variance=False):
        self.__name__ = name
        self.__constraints__ = constraints
        self.__bound__ = bound
        self.__covariant__ = covariant
        self.__contravariant__ = contravariant
        self.__infer_variance__ = infer_variance
        # CPython sets __module__ using caller() - simplified here
        self.__module__ = None

    def __repr__(self):
        if self.__covariant__:
            prefix = '+'
        elif self.__contravariant__:
            prefix = '-'
        else:
            prefix = '~' if self.__infer_variance__ else ''
        return prefix + self.__name__

    def __reduce__(self):
        # For pickling - just return the name
        return self.__name__

    def __hash__(self):
        return hash(self.__name__)

    def __eq__(self, other):
        if isinstance(other, TypeVar):
            return self.__name__ == other.__name__
        return NotImplemented


class ParamSpecArgs:
    """The args for a ParamSpec object.

    Given P = ParamSpec('P'), P.args returns an instance of ParamSpecArgs.

    CPython 3.12: Objects/typevarobject.c (paramspecattrobject)
    """
    __slots__ = ('__origin__',)

    def __init__(self, origin):
        self.__origin__ = origin

    def __repr__(self):
        return f"{self.__origin__.__name__}.args"

    def __eq__(self, other):
        if isinstance(other, ParamSpecArgs):
            return self.__origin__ == other.__origin__
        return NotImplemented

    def __hash__(self):
        return hash((ParamSpecArgs, self.__origin__))


class ParamSpecKwargs:
    """The kwargs for a ParamSpec object.

    Given P = ParamSpec('P'), P.kwargs returns an instance of ParamSpecKwargs.

    CPython 3.12: Objects/typevarobject.c (paramspecattrobject)
    """
    __slots__ = ('__origin__',)

    def __init__(self, origin):
        self.__origin__ = origin

    def __repr__(self):
        return f"{self.__origin__.__name__}.kwargs"

    def __eq__(self, other):
        if isinstance(other, ParamSpecKwargs):
            return self.__origin__ == other.__origin__
        return NotImplemented

    def __hash__(self):
        return hash((ParamSpecKwargs, self.__origin__))


class ParamSpec:
    """Parameter specification variable.

    CPython 3.12: Objects/typevarobject.c (paramspecobject)

    Usage::

        P = ParamSpec('P')

        def add_logging(f: Callable[P, T]) -> Callable[P, T]:
            ...
    """

    def __init__(self, name, *, bound=None, covariant=False,
                 contravariant=False, infer_variance=False):
        self.__name__ = name
        self.__bound__ = bound
        self.__covariant__ = covariant
        self.__contravariant__ = contravariant
        self.__infer_variance__ = infer_variance
        self.__module__ = None

    @property
    def args(self):
        return ParamSpecArgs(self)

    @property
    def kwargs(self):
        return ParamSpecKwargs(self)

    def __repr__(self):
        if self.__covariant__:
            prefix = '+'
        elif self.__contravariant__:
            prefix = '-'
        else:
            prefix = '~' if self.__infer_variance__ else ''
        return prefix + self.__name__

    def __reduce__(self):
        return self.__name__

    def __hash__(self):
        return hash(self.__name__)

    def __eq__(self, other):
        if isinstance(other, ParamSpec):
            return self.__name__ == other.__name__
        return NotImplemented


class TypeVarTuple:
    """Type variable tuple.

    CPython 3.12: Objects/typevarobject.c (typevartupleobject)

    Usage::

        Ts = TypeVarTuple('Ts')

        def move_first_element_to_last(tup: tuple[T, *Ts]) -> tuple[*Ts, T]:
            ...
    """

    def __init__(self, name):
        self.__name__ = name
        self.__module__ = None

    def __repr__(self):
        return self.__name__

    def __reduce__(self):
        return self.__name__

    def __hash__(self):
        return hash(self.__name__)

    def __eq__(self, other):
        if isinstance(other, TypeVarTuple):
            return self.__name__ == other.__name__
        return NotImplemented

    def __iter__(self):
        # CPython: yield typing.Unpack[self]
        # This is called when unpacking *Ts in Generic[*Ts]
        # For now, yield self - typing.py will handle Unpack
        yield self


class TypeAliasType:
    """Type alias.

    CPython 3.12: Objects/typevarobject.c (typealiasobject)

    Type aliases are created through the type statement::

        type Alias = int
    """

    def __init__(self, name, value, *, type_params=()):
        self.__name__ = name
        self.__value__ = value
        self.__type_params__ = type_params
        self.__module__ = None

    def __repr__(self):
        return self.__name__

    def __reduce__(self):
        return self.__name__

    def __hash__(self):
        return hash(self.__name__)

    def __eq__(self, other):
        if isinstance(other, TypeAliasType):
            return self.__name__ == other.__name__ and self.__value__ == other.__value__
        return NotImplemented


class Generic:
    """Abstract base class for generic types.

    CPython 3.12: Modules/_typingmodule.c

    A generic type is typically declared by inheriting from
    this class with one or more type variables::

        class Mapping(Generic[KT, VT]):
            def __getitem__(self, key: KT) -> VT:
                ...

    The actual implementation of __class_getitem__ and __init_subclass__
    is provided by typing.py which overrides these methods.
    """
    __slots__ = ()

    def __init_subclass__(cls, *args, **kwargs):
        pass

    def __class_getitem__(cls, params):
        # typing.py replaces this with _generic_class_getitem
        # For the base _typing module, we need a minimal implementation
        # that just allows Generic[T] syntax to work
        raise NotImplementedError(
            "Generic.__class_getitem__ requires typing module initialization"
        )
