"""Pending: specialized exception constructors and formatting."""
for cls, args in ((NameError, ()), (AttributeError, ()), (KeyError, ('key',)), (OSError, (2, 'missing'))):
    error = cls(*args)
    print(cls.__name__, error.args, str(error), repr(error))
