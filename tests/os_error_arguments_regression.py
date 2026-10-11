"""OSError positional fields and FileNotFoundError selection; no winerror translation."""
for cls in (OSError, FileNotFoundError):
    for args in ((), ('message',), (999, 'problem'), (2, 'missing'),
                 (2, 'missing', 'source'), (999, None), (None, None),
                 ('code', 'message'), (999, 'text', None),
                 (999, 'text', 'source', None),
                 (999, 'text', 'source', None, 'target'),
                 (999, 'text', None, None, 'target'),
                 (1, 2, 3, 4, 5, 6)):
        error = cls(*args)
        print(cls.__name__, type(error).__name__, error.args, str(error), repr(error))
        print('fields', error.errno, error.strerror, error.filename, error.filename2)
    try:
        cls(errno=2)
    except TypeError as error:
        print('keyword', str(error))

error = OSError(999, 'original', 'source')
error.args = ('changed',)
print('args-independent', error.args, str(error))
for name in ('errno', 'strerror', 'filename', 'filename2'):
    marker = []
    setattr(error, name, marker)
    print('identity', name, getattr(error, name) is marker)
    setattr(error, name, None)
    print('none', name, str(error))
    delattr(error, name)
    print('deleted', name, getattr(error, name), str(error))
    delattr(error, name)
    print('deleted-again', name, getattr(error, name))

# __index__ does not select an errno subtype or replace the original argument.
class Number:
    def __index__(self): raise AssertionError('unexpected index')
number = Number()
error = OSError(number, 'message')
print('errno-identity', type(error).__name__, error.errno is number, error.args[0] is number)

if hasattr(OSError(), 'winerror'):
    error = OSError(999, 'text', 'source', 'custom')
    print('winerror-object', error.winerror, error.errno, error.args, str(error))
    error.winerror = None
    print('winerror-none', str(error))
    del error.winerror
    print('winerror-deleted', error.winerror, str(error))
    del error.winerror
    print('winerror-deleted-again', error.winerror)
