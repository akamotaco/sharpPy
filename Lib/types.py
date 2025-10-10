"""
Define names for built-in types that aren't directly accessible as a builtin.
SharpPy simplified version - provides only the types needed by enum.py
"""
import sys

# Basic function and code types
def _f(): pass
FunctionType = type(_f)
LambdaType = type(lambda: None)         # Same as FunctionType
CodeType = type(_f.__code__)

# MappingProxyType - read-only dict proxy (needed by enum)
MappingProxyType = type(type.__dict__)

# SimpleNamespace (needed by some modules)
SimpleNamespace = type(sys.implementation)

# Method types
class _C:
    def _m(self): pass
MethodType = type(_C()._m)

# Builtin types
BuiltinFunctionType = type(len)
BuiltinMethodType = type([].append)

# Descriptor types
WrapperDescriptorType = type(object.__init__)
MethodWrapperType = type(object().__str__)
# MethodDescriptorType = type(str.join)  # SharpPy: str.join not available yet
MethodDescriptorType = type(object.__init__)  # Temporary: use same as WrapperDescriptorType

# DynamicClassAttribute - property that behaves differently for class vs instance
# Simplified implementation for enum.py
class DynamicClassAttribute:
    """Property that behaves differently when accessed from class vs instance"""
    def __init__(self, fget=None, fset=None, fdel=None, doc=None):
        self.fget = fget
        self.fset = fset
        self.fdel = fdel
        if doc is None and fget is not None:
            doc = fget.__doc__
        self.__doc__ = doc

    def __get__(self, obj, objtype=None):
        if obj is None:
            # Called on class
            if self.fget is None:
                raise AttributeError("unreadable attribute")
            return self.fget(objtype)
        # Called on instance
        if self.fget is None:
            raise AttributeError("unreadable attribute")
        return self.fget(obj)

    def __set__(self, obj, value):
        if self.fset is None:
            raise AttributeError("can't set attribute")
        self.fset(obj, value)

    def __delete__(self, obj):
        if self.fdel is None:
            raise AttributeError("can't delete attribute")
        self.fdel(obj)

    def getter(self, fget):
        return type(self)(fget, self.fset, self.fdel, self.__doc__)

    def setter(self, fset):
        return type(self)(self.fget, fset, self.fdel, self.__doc__)

    def deleter(self, fdel):
        return type(self)(self.fget, self.fset, fdel, self.__doc__)

# Additional types that may be needed
# Note: Generator, Coroutine, and AsyncGenerator types are omitted
# because they require full async/await support which is still in development

__all__ = ['FunctionType', 'LambdaType', 'CodeType', 'MappingProxyType',
           'SimpleNamespace', 'DynamicClassAttribute', 'MethodType',
           'BuiltinFunctionType', 'BuiltinMethodType',
           'WrapperDescriptorType', 'MethodWrapperType', 'MethodDescriptorType']
