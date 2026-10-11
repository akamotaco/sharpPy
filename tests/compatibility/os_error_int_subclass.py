"""Pending: integer subclasses in errno selection and Windows translation."""
class Code(int): pass
for value in (2, 11, 20):
    code=Code(value)
    error=OSError(code,'message')
    print(value,type(error).__name__,error.errno is code,error.args)
if hasattr(OSError(),'winerror'):
    code=Code(10035)
    error=OSError(999,'message',None,code)
    print('winerror',type(error).__name__,error.errno,error.winerror is code)
