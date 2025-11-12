"""
dataclasses module - Python 3.12 compatible implementation for SharpPy
"""

import types

# Constants
MISSING = object()

class Field:
    """Represents a single field in a dataclass."""
    def __init__(self, default=MISSING, default_factory=MISSING, init=True, repr=True, hash=None, compare=True, metadata=None):
        self.default = default
        self.default_factory = default_factory
        self.init = init
        self.repr = repr
        self.hash = hash
        self.compare = compare
        self.metadata = metadata if metadata is not None else {}

def field(*, default=MISSING, default_factory=MISSING, init=True, repr=True, hash=None, compare=True, metadata=None):
    """Create a Field object for dataclass fields."""
    if default is not MISSING and default_factory is not MISSING:
        raise ValueError('cannot specify both default and default_factory')
    return Field(default, default_factory, init, repr, hash, compare, metadata)

def _create_init_method(fields):
    """Create __init__ method for dataclass."""
    def __init__(self, *args, **kwargs):
        # Debug: Print what we're doing
        print(f"[DEBUG] dataclass __init__ called with args={args}, kwargs={kwargs}")
        print(f"[DEBUG] self type: {type(self)}, fields: {[f.name for f in fields]}")

        # Handle positional arguments
        for i, field_obj in enumerate(fields):
            if i < len(args):
                print(f"[DEBUG] Setting {field_obj.name} = {args[i]} (from args)")
                setattr(self, field_obj.name, args[i])
            elif field_obj.name in kwargs:
                print(f"[DEBUG] Setting {field_obj.name} = {kwargs[field_obj.name]} (from kwargs)")
                setattr(self, field_obj.name, kwargs[field_obj.name])
            elif field_obj.default is not MISSING:
                print(f"[DEBUG] Setting {field_obj.name} = {field_obj.default} (default)")
                setattr(self, field_obj.name, field_obj.default)
            elif field_obj.default_factory is not MISSING:
                print(f"[DEBUG] Setting {field_obj.name} = factory() (default_factory)")
                setattr(self, field_obj.name, field_obj.default_factory())
            else:
                raise TypeError(f"__init__() missing required argument: '{field_obj.name}'")

        print(f"[DEBUG] __init__ completed, self.__dict__ = {getattr(self, '__dict__', 'NO DICT')}")
    return __init__

def _create_repr_method(cls_name, fields):
    """Create __repr__ method for dataclass."""
    def __repr__(self):
        field_strs = []
        for field_obj in fields:
            if field_obj.repr:
                value = getattr(self, field_obj.name, MISSING)
                field_strs.append(f"{field_obj.name}={value!r}")
        return f"{cls_name}({', '.join(field_strs)})"
    return __repr__

def _create_eq_method(fields):
    """Create __eq__ method for dataclass."""
    def __eq__(self, other):
        if not isinstance(other, self.__class__):
            return NotImplemented
        for field_obj in fields:
            if field_obj.compare:
                if getattr(self, field_obj.name, MISSING) != getattr(other, field_obj.name, MISSING):
                    return False
        return True
    return __eq__

def _process_class(cls, init=True, repr=True, eq=True, order=False, unsafe_hash=False, frozen=False):
    """Process a class to make it a dataclass."""
    # Get field annotations
    annotations = getattr(cls, '__annotations__', {})

    # Create Field objects for each annotation
    fields = []
    for name, field_type in annotations.items():
        # Check if there's already a Field object assigned
        if hasattr(cls, name) and isinstance(getattr(cls, name), Field):
            field_obj = getattr(cls, name)
            field_obj.name = name
            field_obj.type = field_type
        else:
            # Create a new Field with default value if present
            default_value = getattr(cls, name, MISSING)
            field_obj = Field(default=default_value)
            field_obj.name = name
            field_obj.type = field_type
        fields.append(field_obj)

    # Store fields in class
    cls.__dataclass_fields__ = {f.name: f for f in fields}

    # Add __init__ method
    if init:
        cls.__init__ = _create_init_method(fields)

    # Add __repr__ method
    if repr:
        cls.__repr__ = _create_repr_method(cls.__name__, fields)

    # Add __eq__ method
    if eq:
        cls.__eq__ = _create_eq_method(fields)

    # Mark as dataclass
    cls.__dataclass__ = True

    return cls

def dataclass(cls=None, /, *, init=True, repr=True, eq=True, order=False, unsafe_hash=False, frozen=False, match_args=True, kw_only=False, slots=False, weakref_slot=False):
    """
    Add dunder methods based on the fields defined in the class.

    This is a simplified implementation for SharpPy that covers the most
    common dataclass use cases.
    """
    def wrap(cls):
        return _process_class(cls, init, repr, eq, order, unsafe_hash, frozen)

    # Support both @dataclass and @dataclass() usage
    if cls is None:
        return wrap
    else:
        return wrap(cls)

def fields(class_or_instance):
    """Return a list of Field objects for this dataclass."""
    if hasattr(class_or_instance, '__dataclass_fields__'):
        return list(class_or_instance.__dataclass_fields__.values())
    else:
        raise TypeError('fields() should be called on dataclass instances or classes')

def is_dataclass(obj):
    """Return True if obj is a dataclass or an instance of a dataclass."""
    return hasattr(obj, '__dataclass__')

# Exception classes
class FrozenInstanceError(AttributeError):
    """Raised when trying to assign to a field on a frozen dataclass."""
    pass

# Export public API
__all__ = [
    'dataclass',
    'field',
    'Field',
    'fields',
    'is_dataclass',
    'FrozenInstanceError',
    'MISSING'
]