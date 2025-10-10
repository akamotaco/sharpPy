"""
Collections Abstract Base Classes
"""

# Import C extension types for performance (from _collections module)
try:
    from _collections import deque, defaultdict, OrderedDict
    print("Successfully imported from _collections:", deque, defaultdict, OrderedDict)
except ImportError as e:
    print("Failed to import from _collections:", e)
    # For now, we'll create dummy implementations
    class deque:
        def __init__(self, *args):
            raise NotImplementedError("deque fallback not implemented")

    class defaultdict:
        def __init__(self, *args):
            raise NotImplementedError("defaultdict fallback not implemented")

    class OrderedDict:
        def __init__(self, *args):
            raise NotImplementedError("OrderedDict fallback not implemented")

class Counter(dict):
    """Dict subclass for counting hashable objects."""

    def __init__(self, iterable=None, **kwds):
        super().__init__()
        self.update(iterable, **kwds)

    def update(self, iterable=None, **kwds):
        """Like dict.update() but adds counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) + count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) + 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) + count

    def most_common(self, n=None):
        """Return a list of the n most common elements and their counts."""
        if n is None:
            return sorted(self.items(), key=lambda x: x[1], reverse=True)
        return sorted(self.items(), key=lambda x: x[1], reverse=True)[:n]

    def subtract(self, iterable=None, **kwds):
        """Subtract counts instead of replacing them."""
        if iterable is not None:
            if hasattr(iterable, 'items'):
                for elem, count in iterable.items():
                    self[elem] = self.get(elem, 0) - count
            else:
                for elem in iterable:
                    self[elem] = self.get(elem, 0) - 1
        for elem, count in kwds.items():
            self[elem] = self.get(elem, 0) - count

    def __missing__(self, key):
        return 0

    def __repr__(self):
        if not self:
            return f'{self.__class__.__name__}()'
        items = ', '.join(f'{k!r}: {v!r}' for k, v in self.most_common())
        return f'{self.__class__.__name__}({{{items}}})'

# Export public API
__all__ = [
    'deque', 'defaultdict', 'OrderedDict',
    'Counter'
]