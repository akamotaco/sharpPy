"""Common filesystem errno subclasses, compared with CPython 3.12."""
from builtins import PermissionError as ImportedPermission, FileExistsError as ImportedExists
print('builtin-identity', ImportedPermission is PermissionError, ImportedExists is FileExistsError)
for code in (1, 2, 13, 17, 0, 999, -1, 10 ** 80, True, False, 13.0, '13'):
    error = OSError(code, 'message')
    print('selected', code, type(error).__name__, error.args, str(error))
    print('hierarchy', isinstance(error, OSError), isinstance(error, PermissionError), isinstance(error, FileExistsError))
for cls in (PermissionError, FileExistsError):
    for args in ((), ('message',), (2, 'text'), (13, 'text', 'file'), (17, 'text', None), (1, 2, 3, 4, 5, 6)):
        error = cls(*args)
        print('direct', type(error).__name__, error.args, str(error), repr(error), error.filename)
        assert type(error) is cls
    try:
        cls(errno=1)
    except TypeError as error:
        print('keyword', str(error))
    print('bases', cls.__bases__ == (OSError,), issubclass(cls, OSError))
for code in (1, 13, 17):
    try:
        raise OSError(code, 'message', 'file')
    except PermissionError as error:
        print('caught-permission', error.errno, error.filename)
    except FileExistsError as error:
        print('caught-exists', error.errno, error.filename)
    for args in ((code,), (code, 'text', None, None, None, None)):
        print('arity', type(OSError(*args)).__name__)
class Number:
    def __index__(self): raise AssertionError('index must not run')
number = Number()
error = OSError(number, 'text')
print('no-index', type(error).__name__, error.errno is number)
