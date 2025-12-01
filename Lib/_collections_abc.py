"""
Minimal _collections_abc module for SharpPy
Provides abstract base classes for collections
"""

# Minimal Sequence ABC for random.py
class Sequence:
    """Abstract base class for sized, iterable containers that support indexing."""

    __slots__ = ()

    def __getitem__(self, index):
        raise IndexError

    def __iter__(self):
        i = 0
        try:
            while True:
                v = self[i]
                yield v
                i += 1
        except IndexError:
            return

    def __contains__(self, value):
        for v in self:
            if v == value or v is value:
                return True
        return False

    def __reversed__(self):
        for i in reversed(range(len(self))):
            yield self[i]

    def index(self, value, start=0, stop=None):
        if start < 0:
            start = max(len(self) + start, 0)
        if stop is None:
            stop = len(self)
        elif stop < 0:
            stop = max(len(self) + stop, 0)

        for i in range(start, stop):
            if self[i] == value or self[i] is value:
                return i
        raise ValueError(f'{value!r} is not in sequence')

    def count(self, value):
        return sum(1 for v in self if v == value or v is value)

# _check_methods function for os.py compatibility
def _check_methods(C, *methods):
    """Check if class C has all the specified methods."""
    mro = C.__mro__
    for method in methods:
        for B in mro:
            if method in B.__dict__:
                if B.__dict__[method] is None:
                    return NotImplemented
                break
        else:
            return NotImplemented
    return True

# Register builtin types as Sequences
# This is done in CPython's _collections_abc.py
# For SharpPy, we just export the Sequence class
