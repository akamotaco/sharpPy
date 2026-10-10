class Descriptor:
    def __get__(self, instance, owner):
        raise ValueError('lookup')
class Iterable:
    __iter__ = Descriptor()
    def __getitem__(self, index):
        if index == 1:
            raise IndexError
        return 9
assert list(Iterable()) == [9]
print('iterator lookup error fallback passed')
