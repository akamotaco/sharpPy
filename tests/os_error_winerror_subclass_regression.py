"""Int-subclass winerror conversion bypasses Python numeric and hash hooks."""
class Code(int):
    def __int__(self): raise AssertionError('int called')
    def __index__(self): raise AssertionError('index called')
    def __hash__(self): raise AssertionError('hash called')
    def __eq__(self, other): raise AssertionError('eq called')
class Unhashable(int):
    __hash__ = None
class IndexOnly:
    def __index__(self): raise AssertionError('index-only called')
if hasattr(OSError(), 'winerror'):
    for cls in (OSError, FileNotFoundError, PermissionError, FileExistsError, NotADirectoryError, BlockingIOError):
        for value in (0, 2, 5, 80, 267, 10035, 10036, 10037, 999, -2147024894, -2147483648, 2147483647):
            code=Code(value)
            for filename in (None, 'source'):
                args=(999,'message',filename,code,'target')
                error=cls(*args)
                print('converted',cls.__name__,value,type(error).__name__,error.errno,
                      type(error.errno).__name__,error.args, str(error),error.filename2)
                print('identity',error.winerror is code,args[0],args[3] is code)
        for value in (-2147483649,2147483648,10 ** 80):
            code=Code(value)
            try: cls(999,'message',None,code)
            except OverflowError as error: print('overflow',cls.__name__,str(error))
            try: cls(999,'message',None,code,bad=1)
            except TypeError as error: print('keyword',cls.__name__,str(error))
            error=cls(999,'message',None,code,None,None)
            print('six',type(error).__name__,error.args[3] is code,error.errno)
    code=Unhashable(2)
    error=OSError(Code(999),'message',None,code)
    print('unhashable',type(error).__name__,error.errno,error.winerror is code)
    marker=IndexOnly()
    error=OSError(2,'message',None,marker)
    print('no-index',type(error).__name__,error.errno,error.winerror is marker)
else:
    code=Code(2)
    error=OSError(999,'message',None,code)
    print('ignored',type(error).__name__,error.errno,error.args[3] is code)


# Preserve the payload before Windows applies its narrower C-long limit.
class Plain(int): pass
for value in (10 ** 80, -(10 ** 80)):
    direct=Plain(value)
    print('big-payload',str(direct),repr(direct),direct == value)
