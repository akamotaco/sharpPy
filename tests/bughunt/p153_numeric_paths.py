import contextlib
import io
import itertools
import math
import sys
from abc import abstractproperty
from decimal import Decimal
from fractions import Fraction

print((0.1).as_integer_ratio(), (0.5).as_integer_ratio(), (-1.25).as_integer_ratio(), (3.0).as_integer_ratio(), (1e300).as_integer_ratio()[1], (5e-324).as_integer_ratio()[0], (0.0).as_integer_ratio())
print(Decimal(0.1), Fraction(0.1), Decimal(1.1), Fraction(1.5), Fraction(1e-3).limit_denominator(1000))
print(list(itertools.islice(itertools.count(0, 0.5), 3)), list(itertools.islice(itertools.count(1.5), 2)), list(itertools.islice(itertools.count(10, -2), 3)))
print(list(itertools.islice(itertools.count(Fraction(1, 2), Fraction(1, 3)), 2)), list(itertools.islice(itertools.count(2**70), 2)), repr(itertools.count(1)) if False else "")
print(math.gcd(), math.gcd(12), math.gcd(12, 18, 27), math.gcd(-4, 6), math.gcd(2**70, 2**65), math.gcd(0, 0), math.lcm(), math.lcm(4), math.lcm(4, 6, 10), math.lcm(0, 3), math.lcm(2**40, 3**30))
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    print("captured", 1)
    print("line", end="")
print(repr(buf.getvalue()))
old = sys.stdout
sys.stdout = buf
print("again")
sys.stdout = old
print(repr(buf.getvalue()), file=None)


class P(property):
    def __get__(self, obj, owner=None):
        return "sub"


class Q(property):
    pass


class K:
    a = P(lambda self: 1)
    b = Q(lambda self: 2, doc="d")

    @abstractproperty
    def c(self):
        return 3


print(K().a, K().b, type(K.__dict__["b"]).__name__, isinstance(K.__dict__["b"], property), K.__dict__["b"].__doc__, K.__dict__["c"].__isabstractmethod__)
