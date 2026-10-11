"""Native slots must decline foreign operands and preserve subtype dispatch."""
names = ('__lt__', '__le__', '__eq__', '__ne__', '__gt__', '__ge__')

class Foreign:
    def __lt__(self, other): return 'lt'
    def __le__(self, other): return 'le'
    def __eq__(self, other): return 'eq'
    def __ne__(self, other): return 'ne'
    def __gt__(self, other): return 'gt'
    def __ge__(self, other): return 'ge'

x = Foreign()
print('string', 'a' < x, 'a' <= x, 'a' == x, 'a' != x, 'a' > x, 'a' >= x)

class Sequence(tuple):
    def __lt__(self, other): return 'lt'
    def __le__(self, other): return 'le'
    def __eq__(self, other): return 'eq'
    def __ne__(self, other): return 'ne'
    def __gt__(self, other): return 'gt'
    def __ge__(self, other): return 'ge'

s = Sequence((1,))
print('tuple', s < (), s <= (), s == (), s != (), s > (), s >= ())
print('tuple reflected', () < s, () <= s, () == s, () != s, () > s, () >= s)
class Inherited(tuple): pass
print('inherited', Inherited((1, 2)) == (1, 2), Inherited((1,)) < (2,))

class Number(int):
    def __eq__(self, other): raise ValueError('override must not run')
    def __lt__(self, other): raise ValueError('override must not run')

for name in names:
    slot = getattr(int, name)
    for a, b in ((1, 2), (True, 1), (Number(2**100), 2**100), (1, Number(2))):
        print(name, slot(a, b))
    print('foreign', slot(1, 1.0) is NotImplemented, slot(1, x) is NotImplemented)
    for args in ((), (1,), (1, 2, 3), ('x', 1)):
        try: slot(*args)
        except TypeError as error: print(type(error).__name__, str(error))
    try: slot(1, other=2)
    except TypeError as error: print(type(error).__name__, str(error))

class Broken(tuple):
    def __eq__(self, other): raise ValueError('tuple callback')
try: Broken(()) == ()
except ValueError as error: print(str(error))
