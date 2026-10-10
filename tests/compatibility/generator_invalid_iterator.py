"""__iter__ must return an iterator before SEND is reached."""
class BadIterable:
    def __iter__(self):
        return 42

def parent():
    yield from BadIterable()
g = parent()
try:
    next(g)
except TypeError as error:
    assert str(error) == "iter() returned non-iterator of type 'int'"
else:
    assert False, 'invalid iterator accepted'
print('yield from iterator validation: PASS')
