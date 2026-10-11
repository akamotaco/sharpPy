"""Directory errno selection and direct constructors, compared with CPython 3.12."""
from builtins import NotADirectoryError as ImportedNotDir, IsADirectoryError as ImportedIsDir
print('builtin', ImportedNotDir is NotADirectoryError, ImportedIsDir is IsADirectoryError)
for code in (19, 20, 21, 22, 2, 13, 17, 999, -20, 10 ** 80, 20.0, '20', True, False):
    error = OSError(code, 'message', 'source')
    print('selected', code, type(error).__name__, error.args, error.errno, str(error))
    print('instance', isinstance(error, OSError), isinstance(error, NotADirectoryError), isinstance(error, IsADirectoryError))
for cls in (NotADirectoryError, IsADirectoryError):
    print('hierarchy', cls.__bases__ == (OSError,), issubclass(cls, OSError), issubclass(cls, Exception))
    for args in ((), ('message',), (2, 'text'), (20, 'text', 'source'), (21, 'text', None),
                 (999, 'text', 'source', 267, 'target'), (21, 'text', None, 2, 'target'),
                 (1, 2, 3, 4, 5, 6)):
        error = cls(*args)
        assert type(error) is cls
        print('direct', type(error).__name__, error.args, error.errno, error.strerror,
              error.filename, error.filename2, str(error), repr(error))
    try:
        cls(errno=20)
    except TypeError as error: print('keyword', str(error))
for code in (20, 21):
    try: raise OSError(code, 'message')
    except NotADirectoryError as error: print('caught-not-directory', error.errno)
    except IsADirectoryError as error: print('caught-is-directory', error.errno)
    for args in ((code,), (code, 'text', None, None, None, None)):
        print('arity', type(OSError(*args)).__name__)
class Index:
    def __index__(self): raise AssertionError('must not call index')
marker = Index()
error = OSError(marker, 'message')
print('no-index', type(error).__name__, error.errno is marker)
if hasattr(OSError(), 'winerror'):
    for code in (267, -2147024629):
        error = OSError(999, 'message', 'source', code, 'target')
        print('translated', type(error).__name__, error.errno, error.winerror, error.args, str(error))
