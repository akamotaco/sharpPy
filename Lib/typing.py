"""Support for type hints

This module provides runtime support for type hints. The central part of
typing is a _SpecialForm which handles generic types like Union, List, etc.

This is a simplified but functional implementation for SharpPy.
"""

import sys

# Core infrastructure for typing system - manual _SpecialForm pattern
def _create_special_form(name, doc, impl_func):
    """Create a special form manually (SharpPy compatible approach)"""
    def special_form():
        pass

    # Set the essential attributes
    special_form._name = name
    special_form.__module__ = 'typing'
    special_form.__doc__ = doc
    special_form._impl = impl_func

    # Add the __getitem__ method for subscripting support
    def getitem_method(parameters):
        return impl_func(special_form, parameters)

    special_form.__getitem__ = getitem_method

    # Add string representation
    def repr_method():
        return f'typing.{name}'

    special_form.__repr__ = repr_method
    special_form.__str__ = repr_method

    return special_form

# Union implementation function
def _union_impl(self, parameters):
    """Union type; Union[X, Y] means either X or Y."""
    if not isinstance(parameters, tuple):
        parameters = (parameters,)

    # Simple flattening and deduplication
    flattened = []
    for param in parameters:
        flattened.append(param)

    # Remove duplicates while preserving order
    seen = set()
    unique_params = []
    for param in flattened:
        param_str = str(param)
        if param_str not in seen:
            seen.add(param_str)
            unique_params.append(param)

    # Simplify single-element unions
    if len(unique_params) == 1:
        return unique_params[0]

    # Return a simple string representation
    param_names = [str(p) for p in unique_params]
    return f"typing.Union[{', '.join(param_names)}]"

# Optional implementation function
def _optional_impl(self, parameters):
    """Optional type; Optional[X] means Union[X, None]."""
    if isinstance(parameters, tuple) and len(parameters) != 1:
        raise TypeError("Optional requires exactly one argument")

    if isinstance(parameters, tuple):
        param = parameters[0]
    else:
        param = parameters

    return f"typing.Optional[{str(param)}]"

# Container type implementation functions
def _list_impl(self, parameters):
    """Generic list type"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.List[{', '.join(param_names)}]"
    else:
        return f"typing.List[{str(parameters)}]"

def _dict_impl(self, parameters):
    """Generic dict type"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.Dict[{', '.join(param_names)}]"
    else:
        return f"typing.Dict[{str(parameters)}]"

def _tuple_impl(self, parameters):
    """Generic tuple type"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.Tuple[{', '.join(param_names)}]"
    else:
        return f"typing.Tuple[{str(parameters)}]"

def _set_impl(self, parameters):
    """Generic set type"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.Set[{', '.join(param_names)}]"
    else:
        return f"typing.Set[{str(parameters)}]"

def _frozenset_impl(self, parameters):
    """Generic frozenset type"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.FrozenSet[{', '.join(param_names)}]"
    else:
        return f"typing.FrozenSet[{str(parameters)}]"

def _any_impl(self, parameters):
    """Special type indicating an unconstrained type"""
    return "typing.Any"

def _type_impl(self, parameters):
    """A special construct usable to annotate class objects"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.Type[{', '.join(param_names)}]"
    else:
        return f"typing.Type[{str(parameters)}]"

def _callable_impl(self, parameters):
    """Callable type for function annotations"""
    if isinstance(parameters, tuple):
        param_names = [str(p) for p in parameters]
        return f"typing.Callable[{', '.join(param_names)}]"
    else:
        return f"typing.Callable[{str(parameters)}]"

# Create all special forms manually
Union = _create_special_form('Union', 'Union type; Union[X, Y] means either X or Y.', _union_impl)
Optional = _create_special_form('Optional', 'Optional type; Optional[X] means Union[X, None].', _optional_impl)
List = _create_special_form('List', 'Generic list type', _list_impl)
Dict = _create_special_form('Dict', 'Generic dict type', _dict_impl)
Tuple = _create_special_form('Tuple', 'Generic tuple type', _tuple_impl)
Set = _create_special_form('Set', 'Generic set type', _set_impl)
FrozenSet = _create_special_form('FrozenSet', 'Generic frozenset type', _frozenset_impl)
Any = _create_special_form('Any', 'Special type indicating an unconstrained type', _any_impl)
Type = _create_special_form('Type', 'A special construct usable to annotate class objects', _type_impl)
Callable = _create_special_form('Callable', 'Callable type for function annotations', _callable_impl)

# Utility functions
def get_type_hints(obj, globalns=None, localns=None):
    """Return type hints for a callable object.

    This is a simplified implementation that returns the annotations dict.
    """
    if hasattr(obj, '__annotations__'):
        return obj.__annotations__.copy()
    return {}

def cast(typ, val):
    """Cast a value to a type.

    This returns the value unchanged, as it's only used for type checking.
    """
    return val

def overload(func):
    """Decorator for marking overloaded functions.

    This is a no-op at runtime.
    """
    return func

# Constants for metadata handling
WRAPPER_ASSIGNMENTS = ('__module__', '__name__', '__qualname__',
                       '__doc__', '__annotations__')
WRAPPER_UPDATES = ('__dict__',)

# Export commonly used names
__all__ = [
    # Special forms
    'Union', 'Optional', 'Any', 'Type', 'Callable',

    # Generic types
    'List', 'Dict', 'Tuple', 'Set', 'FrozenSet',

    # Utility functions
    'get_type_hints', 'cast', 'overload',

    # Constants
    'WRAPPER_ASSIGNMENTS', 'WRAPPER_UPDATES',
]