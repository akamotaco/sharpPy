"""Random variable generators - SharpPy minimal implementation"""

from _random import Random as _Random

# Create default instance
_inst = _Random()

def seed(a=None):
    """Initialize internal state from hashable object."""
    _inst.seed(a)

def random():
    """random() -> x in the interval [0, 1)."""
    return _inst.random()

def randint(a, b):
    """Return random integer in range [a, b], including both end points."""
    return int(_inst.random() * (b - a + 1)) + a

def choice(seq):
    """Choose a random element from a non-empty sequence."""
    return seq[int(_inst.random() * len(seq))]

def shuffle(x):
    """Shuffle list x in place, and return None."""
    for i in range(len(x) - 1, 0, -1):
        j = int(_inst.random() * (i + 1))
        x[i], x[j] = x[j], x[i]

__all__ = ['seed', 'random', 'randint', 'choice', 'shuffle', 'Random']

# Expose Random class
Random = _Random
