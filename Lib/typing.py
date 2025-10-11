"""typing - Type hints support for Python 3.12

This module provides runtime support for type hints as specified by PEP 484, 585, and related PEPs.
"""

# CPython 3.12 compatibility: Generic alias support
class _GenericAlias:
    """Generic alias for parameterized types like List[int], Dict[str, int]"""

    def __init__(self, origin, args):
        self.__origin__ = origin
        self.__args__ = args if isinstance(args, tuple) else (args,)
        self.__module__ = 'typing'

    def __repr__(self):
        # Format: typing.List[str] or typing.Dict[str, int]
        if self.__origin__.__module__ == 'builtins':
            origin_name = self.__origin__.__name__
        else:
            origin_name = f"{self.__origin__.__module__}.{self.__origin__.__name__}"

        # Find the typing name (List instead of list, Dict instead of dict)
        typing_name = _ORIGIN_TO_TYPING_NAME.get(self.__origin__, origin_name)

        if len(self.__args__) == 1:
            args_str = repr(self.__args__[0])
        else:
            args_str = ', '.join(repr(arg) for arg in self.__args__)

        return f"typing.{typing_name}[{args_str}]"

    def __str__(self):
        return self.__repr__()

    def __eq__(self, other):
        if not isinstance(other, _GenericAlias):
            return NotImplemented
        return self.__origin__ == other.__origin__ and self.__args__ == other.__args__

    def __hash__(self):
        return hash((self.__origin__, self.__args__))


class _SpecialGenericAlias:
    """Special generic alias for typing constructs like List, Dict before parameterization"""

    def __init__(self, origin, name):
        self.__origin__ = origin
        self.__name__ = name
        self.__module__ = 'typing'

    def __repr__(self):
        return f"typing.{self.__name__}"

    def __str__(self):
        return self.__repr__()

    def __getitem__(self, params):
        """Support subscripting: List[int], Dict[str, int]"""
        # Handle single parameter
        if not isinstance(params, tuple):
            params = (params,)

        return _GenericAlias(self.__origin__, params)

    def __eq__(self, other):
        if isinstance(other, _SpecialGenericAlias):
            return self.__origin__ == other.__origin__ and self.__name__ == other.__name__
        return NotImplemented

    def __hash__(self):
        return hash((self.__origin__, self.__name__))


# Mapping from origin type to typing name
_ORIGIN_TO_TYPING_NAME = {}

# Create special generic aliases for common types
List = _SpecialGenericAlias(list, 'List')
Dict = _SpecialGenericAlias(dict, 'Dict')
Set = _SpecialGenericAlias(set, 'Set')
Tuple = _SpecialGenericAlias(tuple, 'Tuple')
FrozenSet = _SpecialGenericAlias(frozenset, 'FrozenSet')

# Register mappings
_ORIGIN_TO_TYPING_NAME[list] = 'List'
_ORIGIN_TO_TYPING_NAME[dict] = 'Dict'
_ORIGIN_TO_TYPING_NAME[set] = 'Set'
_ORIGIN_TO_TYPING_NAME[tuple] = 'Tuple'
_ORIGIN_TO_TYPING_NAME[frozenset] = 'FrozenSet'


# Special forms that don't use _GenericAlias
class _SpecialForm:
    """Special form for Union, Optional, etc."""

    def __init__(self, name):
        self.__name__ = name
        self.__module__ = 'typing'

    def __repr__(self):
        return f"typing.{self.__name__}"

    def __str__(self):
        return self.__repr__()

    def __getitem__(self, params):
        """Support Union[int, str], Optional[int]"""
        if not isinstance(params, tuple):
            params = (params,)

        # Special handling for Optional
        if self.__name__ == 'Optional':
            # Optional[X] is Union[X, None]
            return _UnionGenericAlias((params[0], type(None)))

        # Union
        if self.__name__ == 'Union':
            return _UnionGenericAlias(params)

        # Generic special form
        return _GenericAlias(self, params)


class _UnionGenericAlias:
    """Generic alias for Union types"""

    def __init__(self, args):
        self.__args__ = args
        self.__module__ = 'typing'

    def __repr__(self):
        # Check if this is Optional (Union[X, None])
        if len(self.__args__) == 2 and type(None) in self.__args__:
            # This is Optional[X]
            other_arg = self.__args__[0] if self.__args__[1] is type(None) else self.__args__[1]
            return f"typing.Optional[{repr(other_arg)}]"

        # Regular Union
        args_str = ', '.join(repr(arg) for arg in self.__args__)
        return f"typing.Union[{args_str}]"

    def __str__(self):
        return self.__repr__()

    def __eq__(self, other):
        if not isinstance(other, _UnionGenericAlias):
            return NotImplemented
        return set(self.__args__) == set(other.__args__)

    def __hash__(self):
        return hash(frozenset(self.__args__))


# Create special forms
Union = _SpecialForm('Union')
Optional = _SpecialForm('Optional')

# Simple stubs for other typing constructs
Any = object
Callable = object
