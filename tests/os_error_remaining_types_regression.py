"""CPython 3.12 errno selection and native OSError hierarchy."""
types = (ConnectionError, BrokenPipeError, ConnectionAbortedError,
         ConnectionRefusedError, ConnectionResetError, ChildProcessError,
         InterruptedError, ProcessLookupError, TimeoutError)
windows = hasattr(OSError(), 'winerror')
for cls in types:
    print('hierarchy', cls.__name__, issubclass(cls, OSError),
          issubclass(cls, ConnectionError))
    for args in ((), ('message',), (2, 'message'),
                 (32, 'message', 'source'), (999, 'message', 'source', 109, 'dest'),
                 (1, 2, 3, 4, 5, 6)):
        error = cls(*args)
        print('direct', cls.__name__, type(error).__name__, error.args,
              error.errno, error.strerror, error.filename, error.filename2, str(error))
    try:
        cls(unexpected=1)
    except TypeError as error:
        print('kwargs', cls.__name__, str(error))
    try:
        raise cls(32, 'message')
    except ConnectionError as error:
        print('caught-connection', type(error).__name__)
    except OSError as error:
        print('caught-os', type(error).__name__)

# Cover both host socket numbers and foreign-platform numbers: only host
# numbers select a subtype. Avoid an errno module dependency in SharpPy.
for code in (0, 1, 2, 3, 4, 10, 11, 13, 17, 20, 21, 32,
             35, 36, 37, 53, 54, 58, 60, 61,
             103, 104, 108, 110, 111, 114, 115,
             10035, 10036, 10037, 10053, 10054, 10058, 10060, 10061,
             999, -1, 10**80, -(10**80), True, False, 3.0, '3', None):
    error = OSError(code, 'message')
    print('select', repr(code), type(error).__name__, error.errno is code, error.args)
    print('one', type(OSError(code)).__name__)
    print('six', type(OSError(code, 2, 3, 4, 5, 6)).__name__)
if windows:
    for code in (109, 232, 128, 129, 10004, 10053, 10054, 10058, 10060, 10061):
        error = OSError(999, 'message', 'file', code)
        print('winerror', code, type(error).__name__, error.errno, str(error))
