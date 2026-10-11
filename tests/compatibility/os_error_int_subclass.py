"""Pending: integer subclasses and hash/equality callbacks in errno selection."""
class Code(int): pass
for value in (2, 11, 20):
    code=Code(value)
    error=OSError(code,'message')
    print(value,type(error).__name__,error.errno is code,error.args)

class Hashed(int):
    def __hash__(self):
        print('hash-called')
        return 2
    def __eq__(self, other):
        print('eq-called',other)
        return True
error=OSError(Hashed(999),'message')
print('custom',type(error).__name__,error.errno)
class Unhashable(int):
    __hash__=None
try:
    error=OSError(Unhashable(2),'message')
    print('unhashable-result',type(error).__name__)
except TypeError as error: print('unhashable-error',str(error))
