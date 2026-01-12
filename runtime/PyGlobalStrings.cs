using System;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Include/internal/pycore_global_strings.h
    /// Pre-interned global strings for fast pointer comparison.
    /// All strings are interned at class initialization time.
    /// </summary>
    public static class PyGlobalStrings
    {
        // ============================================================
        // Literals (anonymous scope names, special strings)
        // CPython: _Py_global_strings.literals
        // ============================================================
        public static class Literals
        {
            // Comprehension/generator scope names
            public static readonly string DictComp = string.Intern("<dictcomp>");
            public static readonly string GenExpr = string.Intern("<genexpr>");
            public static readonly string Lambda = string.Intern("<lambda>");
            public static readonly string ListComp = string.Intern("<listcomp>");
            public static readonly string Module = string.Intern("<module>");
            public static readonly string SetComp = string.Intern("<setcomp>");

            // Other special names
            public static readonly string String = string.Intern("<string>");
            public static readonly string Unknown = string.Intern("<unknown>");
            public static readonly string Locals = string.Intern(".<locals>");
            public static readonly string Empty = string.Intern("");
        }

        // ============================================================
        // Identifiers (magic methods, dunder attributes)
        // CPython: _Py_ID() macro → _Py_global_strings.identifiers
        // ============================================================
        public static class Id
        {
            // Class/type related
            public static readonly string __class__ = string.Intern("__class__");
            public static readonly string __bases__ = string.Intern("__bases__");
            public static readonly string __mro__ = string.Intern("__mro__");
            public static readonly string __subclasses__ = string.Intern("__subclasses__");
            public static readonly string __metaclass__ = string.Intern("__metaclass__");

            // Object identity
            public static readonly string __name__ = string.Intern("__name__");
            public static readonly string __qualname__ = string.Intern("__qualname__");
            public static readonly string __module__ = string.Intern("__module__");
            public static readonly string __doc__ = string.Intern("__doc__");
            public static readonly string __dict__ = string.Intern("__dict__");
            public static readonly string __slots__ = string.Intern("__slots__");
            public static readonly string __weakref__ = string.Intern("__weakref__");

            // Construction/destruction
            public static readonly string __new__ = string.Intern("__new__");
            public static readonly string __init__ = string.Intern("__init__");
            public static readonly string __del__ = string.Intern("__del__");
            public static readonly string __init_subclass__ = string.Intern("__init_subclass__");
            public static readonly string __class_getitem__ = string.Intern("__class_getitem__");

            // Metaclass
            public static readonly string __prepare__ = string.Intern("__prepare__");
            public static readonly string __set_name__ = string.Intern("__set_name__");
            public static readonly string __classcell__ = string.Intern("__classcell__");

            // Representation
            public static readonly string __repr__ = string.Intern("__repr__");
            public static readonly string __str__ = string.Intern("__str__");
            public static readonly string __bytes__ = string.Intern("__bytes__");
            public static readonly string __format__ = string.Intern("__format__");

            // Comparison
            public static readonly string __lt__ = string.Intern("__lt__");
            public static readonly string __le__ = string.Intern("__le__");
            public static readonly string __eq__ = string.Intern("__eq__");
            public static readonly string __ne__ = string.Intern("__ne__");
            public static readonly string __gt__ = string.Intern("__gt__");
            public static readonly string __ge__ = string.Intern("__ge__");
            public static readonly string __hash__ = string.Intern("__hash__");
            public static readonly string __bool__ = string.Intern("__bool__");

            // Attribute access
            public static readonly string __getattr__ = string.Intern("__getattr__");
            public static readonly string __getattribute__ = string.Intern("__getattribute__");
            public static readonly string __setattr__ = string.Intern("__setattr__");
            public static readonly string __delattr__ = string.Intern("__delattr__");
            public static readonly string __dir__ = string.Intern("__dir__");

            // Descriptor protocol
            public static readonly string __get__ = string.Intern("__get__");
            public static readonly string __set__ = string.Intern("__set__");
            public static readonly string __delete__ = string.Intern("__delete__");

            // Container methods
            public static readonly string __len__ = string.Intern("__len__");
            public static readonly string __length_hint__ = string.Intern("__length_hint__");
            public static readonly string __getitem__ = string.Intern("__getitem__");
            public static readonly string __setitem__ = string.Intern("__setitem__");
            public static readonly string __delitem__ = string.Intern("__delitem__");
            public static readonly string __missing__ = string.Intern("__missing__");
            public static readonly string __contains__ = string.Intern("__contains__");
            public static readonly string __iter__ = string.Intern("__iter__");
            public static readonly string __next__ = string.Intern("__next__");
            public static readonly string __reversed__ = string.Intern("__reversed__");

            // Numeric methods
            public static readonly string __add__ = string.Intern("__add__");
            public static readonly string __radd__ = string.Intern("__radd__");
            public static readonly string __iadd__ = string.Intern("__iadd__");
            public static readonly string __sub__ = string.Intern("__sub__");
            public static readonly string __rsub__ = string.Intern("__rsub__");
            public static readonly string __isub__ = string.Intern("__isub__");
            public static readonly string __mul__ = string.Intern("__mul__");
            public static readonly string __rmul__ = string.Intern("__rmul__");
            public static readonly string __imul__ = string.Intern("__imul__");
            public static readonly string __truediv__ = string.Intern("__truediv__");
            public static readonly string __rtruediv__ = string.Intern("__rtruediv__");
            public static readonly string __itruediv__ = string.Intern("__itruediv__");
            public static readonly string __floordiv__ = string.Intern("__floordiv__");
            public static readonly string __rfloordiv__ = string.Intern("__rfloordiv__");
            public static readonly string __ifloordiv__ = string.Intern("__ifloordiv__");
            public static readonly string __mod__ = string.Intern("__mod__");
            public static readonly string __rmod__ = string.Intern("__rmod__");
            public static readonly string __imod__ = string.Intern("__imod__");
            public static readonly string __pow__ = string.Intern("__pow__");
            public static readonly string __rpow__ = string.Intern("__rpow__");
            public static readonly string __ipow__ = string.Intern("__ipow__");
            public static readonly string __neg__ = string.Intern("__neg__");
            public static readonly string __pos__ = string.Intern("__pos__");
            public static readonly string __abs__ = string.Intern("__abs__");
            public static readonly string __invert__ = string.Intern("__invert__");

            // Bitwise methods
            public static readonly string __lshift__ = string.Intern("__lshift__");
            public static readonly string __rlshift__ = string.Intern("__rlshift__");
            public static readonly string __ilshift__ = string.Intern("__ilshift__");
            public static readonly string __rshift__ = string.Intern("__rshift__");
            public static readonly string __rrshift__ = string.Intern("__rrshift__");
            public static readonly string __irshift__ = string.Intern("__irshift__");
            public static readonly string __and__ = string.Intern("__and__");
            public static readonly string __rand__ = string.Intern("__rand__");
            public static readonly string __iand__ = string.Intern("__iand__");
            public static readonly string __or__ = string.Intern("__or__");
            public static readonly string __ror__ = string.Intern("__ror__");
            public static readonly string __ior__ = string.Intern("__ior__");
            public static readonly string __xor__ = string.Intern("__xor__");
            public static readonly string __rxor__ = string.Intern("__rxor__");
            public static readonly string __ixor__ = string.Intern("__ixor__");

            // Type conversion
            public static readonly string __int__ = string.Intern("__int__");
            public static readonly string __float__ = string.Intern("__float__");
            public static readonly string __complex__ = string.Intern("__complex__");
            public static readonly string __index__ = string.Intern("__index__");
            public static readonly string __round__ = string.Intern("__round__");
            public static readonly string __trunc__ = string.Intern("__trunc__");
            public static readonly string __floor__ = string.Intern("__floor__");
            public static readonly string __ceil__ = string.Intern("__ceil__");

            // Context manager
            public static readonly string __enter__ = string.Intern("__enter__");
            public static readonly string __exit__ = string.Intern("__exit__");
            public static readonly string __aenter__ = string.Intern("__aenter__");
            public static readonly string __aexit__ = string.Intern("__aexit__");

            // Callable
            public static readonly string __call__ = string.Intern("__call__");

            // Awaitable/async
            public static readonly string __await__ = string.Intern("__await__");
            public static readonly string __aiter__ = string.Intern("__aiter__");
            public static readonly string __anext__ = string.Intern("__anext__");

            // Function/method
            public static readonly string __func__ = string.Intern("__func__");
            public static readonly string __self__ = string.Intern("__self__");
            public static readonly string __wrapped__ = string.Intern("__wrapped__");
            public static readonly string __closure__ = string.Intern("__closure__");
            public static readonly string __globals__ = string.Intern("__globals__");
            public static readonly string __code__ = string.Intern("__code__");
            public static readonly string __defaults__ = string.Intern("__defaults__");
            public static readonly string __kwdefaults__ = string.Intern("__kwdefaults__");
            public static readonly string __annotations__ = string.Intern("__annotations__");

            // Module
            public static readonly string __package__ = string.Intern("__package__");
            public static readonly string __spec__ = string.Intern("__spec__");
            public static readonly string __loader__ = string.Intern("__loader__");
            public static readonly string __path__ = string.Intern("__path__");
            public static readonly string __file__ = string.Intern("__file__");
            public static readonly string __cached__ = string.Intern("__cached__");
            public static readonly string __builtins__ = string.Intern("__builtins__");
            public static readonly string __all__ = string.Intern("__all__");
            public static readonly string __main__ = string.Intern("__main__");

            // Exception
            public static readonly string __cause__ = string.Intern("__cause__");
            public static readonly string __context__ = string.Intern("__context__");
            public static readonly string __traceback__ = string.Intern("__traceback__");
            public static readonly string __suppress_context__ = string.Intern("__suppress_context__");

            // Pickle
            public static readonly string __reduce__ = string.Intern("__reduce__");
            public static readonly string __reduce_ex__ = string.Intern("__reduce_ex__");
            public static readonly string __getstate__ = string.Intern("__getstate__");
            public static readonly string __setstate__ = string.Intern("__setstate__");
            public static readonly string __getnewargs__ = string.Intern("__getnewargs__");
            public static readonly string __getnewargs_ex__ = string.Intern("__getnewargs_ex__");

            // Copy
            public static readonly string __copy__ = string.Intern("__copy__");
            public static readonly string __deepcopy__ = string.Intern("__deepcopy__");

            // Other
            public static readonly string __sizeof__ = string.Intern("__sizeof__");
            public static readonly string __match_args__ = string.Intern("__match_args__");

            // Common non-dunder names
            public static readonly string self = string.Intern("self");
            public static readonly string cls = string.Intern("cls");
        }

        // ============================================================
        // Helper methods for reference equality comparison
        // ============================================================

        /// <summary>
        /// Fast reference equality check for interned strings.
        /// CPython: Uses pointer comparison via &_Py_ID(name)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ReferenceEquals(string? a, string b)
        {
            return object.ReferenceEquals(a, b);
        }

        /// <summary>
        /// Intern a string and check if it matches a known identifier.
        /// Use this when comparing user input against known magic methods.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Intern(string s)
        {
            return string.Intern(s);
        }
    }
}
