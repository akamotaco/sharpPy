"""
_typing - C extension module stub for typing support (CPython 3.12 compatible)

This module provides Python implementations of the types that are normally
implemented in C in CPython's _typing module (Modules/_typingmodule.c).

SharpPy implements these as pure Python stubs to support CPython 3.12's typing.py.
"""

# CPython 3.12: Modules/_typingmodule.c
import sys
from types import GenericAlias

_union_type_or = type(int | str).__or__


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
        self._constraints = constraints
        self._bound = bound
        self._evaluate_constraints = None
        self._evaluate_bound = None
        self.__covariant__ = covariant
        self.__contravariant__ = contravariant
        self.__infer_variance__ = infer_variance
        # CPython sets __module__ using caller() - simplified here
        self.__module__ = None

    @classmethod
    def _from_type_param(cls, name, evaluate_bound, evaluate_constraints):
        # A parameter of class C[T: bound]: the bound waits until someone asks (PEP 695).
        self = cls(name, infer_variance=True)
        self._evaluate_bound = evaluate_bound
        self._evaluate_constraints = evaluate_constraints
        return self

    @property
    def __bound__(self):
        if self._evaluate_bound is not None:
            self._bound = self._evaluate_bound()
            self._evaluate_bound = None
        return self._bound

    @property
    def __constraints__(self):
        if self._evaluate_constraints is not None:
            self._constraints = self._evaluate_constraints()
            self._evaluate_constraints = None
        return self._constraints

    def __repr__(self):
        if self.__infer_variance__:
            prefix = ''
        elif self.__covariant__:
            prefix = '+'
        elif self.__contravariant__:
            prefix = '-'
        else:
            prefix = '~'
        return prefix + self.__name__

    def __typing_subst__(self, arg):
        import typing
        return typing._typevar_subst(self, arg)

    def __mro_entries__(self, bases):
        raise TypeError("Cannot subclass an instance of TypeVar")

    def __reduce__(self):
        # For pickling - just return the name
        return self.__name__

    # typevar_or: typing._make_union, so a string or None on either side works.
    def __or__(self, other):
        import typing
        return typing._make_union(self, other)

    def __ror__(self, other):
        import typing
        return typing._make_union(other, self)


class ParamSpecArgs:
    """The args for a ParamSpec object.

    Given P = ParamSpec('P'), P.args returns an instance of ParamSpecArgs.

    CPython 3.12: Objects/typevarobject.c (paramspecattrobject)
    """
    __slots__ = ('__origin__', '__weakref__')

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
    __slots__ = ('__origin__', '__weakref__')

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
        if self.__infer_variance__:
            prefix = ''
        elif self.__covariant__:
            prefix = '+'
        elif self.__contravariant__:
            prefix = '-'
        else:
            prefix = '~'
        return prefix + self.__name__

    def __typing_subst__(self, arg):
        import typing
        return typing._paramspec_subst(self, arg)

    def __typing_prepare_subst__(self, alias, args):
        import typing
        return typing._paramspec_prepare_subst(self, alias, args)

    def __mro_entries__(self, bases):
        raise TypeError("Cannot subclass an instance of ParamSpec")

    def __reduce__(self):
        return self.__name__

    # typevar_or: typing._make_union, so a string or None on either side works.
    def __or__(self, other):
        import typing
        return typing._make_union(self, other)

    def __ror__(self, other):
        import typing
        return typing._make_union(other, self)


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

    def __typing_subst__(self, arg):
        raise TypeError("Substitution of bare TypeVarTuple is not supported")

    def __typing_prepare_subst__(self, alias, args):
        import typing
        return typing._typevartuple_prepare_subst(self, alias, args)

    def __mro_entries__(self, bases):
        raise TypeError("Cannot subclass an instance of TypeVarTuple")

    def __reduce__(self):
        return self.__name__

    def __iter__(self):
        import typing
        yield typing.Unpack[self]


class TypeAliasType:
    """Type alias. CPython 3.12: Objects/typevarobject.c."""

    __slots__ = ('_name', '_type_params', '_compute_value', '_value', '_module')

    def __init__(self, name, value, *, type_params=None):
        if type_params is not None and not isinstance(type_params, tuple):
            raise TypeError("type_params must be a tuple")
        self._alloc(name, type_params, None, (value,), sys._getframe(1).f_globals.get('__name__'))

    def _alloc(self, name, type_params, compute_value, value, module):
        object.__setattr__(self, '_name', name)
        object.__setattr__(self, '_type_params', type_params or None)
        object.__setattr__(self, '_compute_value', compute_value)
        object.__setattr__(self, '_value', value)
        object.__setattr__(self, '_module', module)

    @classmethod
    def _make(cls, name, type_params, compute_value):
        self = cls.__new__(cls)
        self._alloc(name, type_params, compute_value, None, None)
        return self

    def __init_subclass__(cls, **kwargs):
        raise TypeError("type 'typing.TypeAliasType' is not an acceptable base type")

    def __setattr__(self, name, value):
        if name in ('__name__', '__value__', '__type_params__', '__parameters__'):
            raise AttributeError(f"attribute '{name}' of 'typing.TypeAliasType' objects is not writable")
        raise AttributeError(f"'typing.TypeAliasType' object has no attribute '{name}'")

    @property
    def __name__(self):
        return self._name

    @property
    def __value__(self):
        if self._value is None:
            object.__setattr__(self, '_value', (self._compute_value(),))
        return self._value[0]

    @property
    def __type_params__(self):
        return self._type_params or ()

    @property
    def __parameters__(self):
        result = []
        for parameter in self._type_params or ():
            if isinstance(parameter, TypeVarTuple):
                result.extend(parameter)
            else:
                result.append(parameter)
        return tuple(result)

    def __getattribute__(self, name):
        if name == '__module__':
            module = object.__getattribute__(self, '_module')
            if module is not None:
                return module
            return getattr(object.__getattribute__(self, '_compute_value'), '__module__', None)
        return object.__getattribute__(self, name)

    def __repr__(self):
        return self._name

    def __reduce__(self):
        return self._name

    def __getitem__(self, args):
        if self._type_params is None:
            raise TypeError("Only generic type aliases are subscriptable")
        return GenericAlias(self, args)

    def __or__(self, other):
        return _union_type_or(self, other)

    def __ror__(self, other):
        return _union_type_or(other, self)


TypeAliasType.__module__ = 'typing'


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
    __module__ = 'typing'

    def __init_subclass__(cls, *args, **kwargs):
        import typing
        typing._generic_init_subclass(cls, *args, **kwargs)

    def __class_getitem__(cls, params):
        import typing
        return typing._generic_class_getitem(cls, params)
