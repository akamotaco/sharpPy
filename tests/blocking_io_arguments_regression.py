"""BlockingIOError constructors and OSError written-state regression."""
for cls in (OSError, BlockingIOError):
    for third in (0, 3, -1, None, 'file', 1.5):
        try:
            error = cls(11, 'would block', third)
            print(cls.__name__, third, type(error).__name__, error.args, error.filename)
            try: print('written', error.characters_written)
            except AttributeError as missing: print('missing', str(missing))
        except Exception as error:
            print('error', cls.__name__, third, type(error).__name__, str(error))

from builtins import BlockingIOError as Imported
print('identity', Imported is BlockingIOError, BlockingIOError.__bases__ == (OSError,))
for code in (11, 35, 36, 37, 114, 115, 10035, 10036, 10037, 999, 10 ** 80, 11.0, '11'):
    error = OSError(code, 'message')
    print('selected', code, type(error).__name__, error.args)
for cls in (OSError, BlockingIOError, FileNotFoundError):
    for args in ((), ('message',), (11, 'text'), (11, 'text', True), (11, 'text', -2),
                 (11, 'text', 3, None, 'target'), (11, 'text', None, None, 'target'),
                 (999, 'text', 3, 10035, 'target'), (11, 'text', 3, 2, 'target'),
                 (1, 2, 3, 4, 5, 6)):
        error=cls(*args)
        print('args',cls.__name__,type(error).__name__,error.args,error.filename,error.filename2,
              str(error),repr(error),getattr(error,'characters_written','missing'))
    try: cls(errno=11)
    except TypeError as error: print('keyword',str(error))
    error=cls(999,'message','source',None,'target')
    for value in (0, 3, -2, -1, True, None, 1.5, '3', 10 ** 80, -(10 ** 80)):
        try: error.characters_written=value
        except Exception as failure: print('set-error',type(failure).__name__,str(failure))
        print('state',getattr(error,'characters_written','missing'),error.filename2,str(error))
    error.characters_written=9
    error.filename2='changed'
    print('dual',error.characters_written,error.filename2,str(error))
    del error.filename2
    print('del-filename2',error.characters_written,error.filename2)
    error.filename2='restored'
    del error.characters_written
    print('del-written',error.filename2,getattr(error,'characters_written','missing'))
    try: del error.characters_written
    except AttributeError as missing: print('double-delete',str(missing))

class Index:
    def __index__(self):
        print('index-called')
        return 7
class IntOnly:
    def __int__(self): raise AssertionError('must not call int')
class FloatOnly:
    def __float__(self): raise AssertionError('must not call float')
class AddOnly:
    def __add__(self, other): raise AssertionError('must not add')
    def __repr__(self): return 'AddOnly()'
class BadIndex:
    def __index__(self): return 'bad'
class HugeIndex:
    def __index__(self): return 10 ** 80
class RaisedIndex:
    def __index__(self): raise RuntimeError('index failed')
for cls in (Index, IntOnly, FloatOnly, AddOnly, BadIndex, HugeIndex, RaisedIndex):
    obj=cls()
    try:
        error=BlockingIOError(11,'message',obj)
        print('protocol',cls.__name__,error.filename is obj,getattr(error,'characters_written','missing'))
    except Exception as error: print('protocol-error',cls.__name__,type(error).__name__,str(error))
    error=OSError()
    error.characters_written=5
    try: error.characters_written=obj
    except Exception as failure: print('setter-error',cls.__name__,type(failure).__name__,str(failure))
    print('setter-state',error.characters_written)
# The index callback may update the same auxiliary state before assignment completes.
error=OSError(999,'message','source',None,'old')
class Reenter:
    def __index__(self):
        error.filename2='callback'
        error.characters_written=2
        return 8
error.characters_written=Reenter()
print('reentry',error.filename2,error.characters_written)
try: raise BlockingIOError(11,'blocked',4)
except BlockingIOError as error: print('caught',error.characters_written)

# Reading a C ssize_t yields a fresh Python int except for the small-int cache.
error=OSError()
import sys
for value in (-6, -5, 0, 256, 257, sys.maxsize, -sys.maxsize - 1):
    error.characters_written=value
    first=error.characters_written
    second=error.characters_written
    print('read-value',first,type(first).__name__,first is second)
