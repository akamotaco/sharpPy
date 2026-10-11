"""Windows translation and argument preservation, compared with CPython 3.12."""
# Direct subtype construction isolates translation from pending errno subtypes.
for code in list(range(-2, 270)) + [1113, 1816, 9999, 10000, 10004, 10009, 10013, 10014, 10022, 10024, 10035, 10054, 11999, 12000, -2147024894, -2147024891, -2147024713, -2147483648, 2147483647]:
    error = FileNotFoundError(999, 'message', None, code)
    print('mapping', code, error.errno, error.args, str(error))
for cls in (OSError, FileNotFoundError, PermissionError, FileExistsError):
    for code in (2, 3, 5, 80, 183, 999, True, False, None, '2', 2.0, -2147024894):
        for filename in (None, 'source'):
            args = (999, 'message', filename, code, 'target')
            error = cls(*args)
            print('fields', cls.__name__, type(error).__name__, error.errno, error.args,
                  error.filename, error.filename2, str(error), repr(error))
            print('input', args, getattr(error, 'winerror', None) is code)
    for code in (-2147483649, 2147483648, 10 ** 80, -(10 ** 80)):
        try:
            error = cls(999, 'message', None, code)
            print('unconverted', error.args)
        except OverflowError as error:
            print('overflow', cls.__name__, str(error))
        try:
            cls(999, 'message', None, code, bad=1)
        except TypeError as error:
            print('keyword-first', str(error))
    error = cls(999, 'message', None, 10 ** 80, None, None)
    print('six-args', error.args, error.errno)
class Index:
    def __index__(self): raise AssertionError('must not call index')
marker = Index()
error = OSError(2, 'message', None, marker)
print('no-index', type(error).__name__, error.errno, getattr(error, 'winerror', marker) is marker)
error = OSError(999, 'message', 'file', 2)
error.errno = 17
if hasattr(error, 'winerror'): error.winerror = 5
print('no-reselection', type(error).__name__, error.errno, error.args, str(error))
