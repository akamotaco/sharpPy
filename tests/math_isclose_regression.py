import math

values = [0.0, -0.0, 1.0, 1.0000000001, -1.0, 1e300, -1e300, float('inf'), -float('inf'), float('nan')]
tolerances = [(1e-9, 0.0), (0.0, 1e-8), (float('inf'), 0.0), (float('nan'), 0.0), (0.0, float('nan'))]
for relative, absolute in tolerances:
    print([math.isclose(a, b, rel_tol=relative, abs_tol=absolute) for a in values for b in values])

def show(label, action):
    try: print(label, action())
    except Exception as error: print(label, type(error).__name__, str(error))
for label, action in (
    ('zero', lambda: math.isclose()),
    ('one', lambda: math.isclose(1)),
    ('three', lambda: math.isclose(1, 1, 0)),
    ('named', lambda: math.isclose(a=1, b=1)),
    ('duplicate', lambda: math.isclose(1, 1, a=1)),
    ('unknown', lambda: math.isclose(1, 1, spam=1)),
    ('missing-unknown', lambda: math.isclose(spam=1)),
    ('missing-b', lambda: math.isclose(1, spam=1)),
    ('duplicates-unknown', lambda: math.isclose(1, 1, spam=1, a=1)),
    ('string', lambda: math.isclose('1', 1)),
    ('negative', lambda: math.isclose(1, 1, rel_tol=-1)),
    ('none', lambda: math.isclose(1, 1, abs_tol=None)),
    ('overflow', lambda: math.isclose(10**1000, 1)),
): show(label, action)

events = []
class Number:
    def __init__(self, label, value): self.label, self.value = label, value
    def __float__(self):
        events.append(self.label)
        return self.value
print(math.isclose(Number('a', 1.0), Number('b', 1.1),
                   abs_tol=Number('abs', 0.0), rel_tol=Number('rel', 0.2)))
print(events)
class Index:
    def __index__(self): return 1
print(math.isclose(Index(), True))
class BadFloat:
    def __float__(self): return 1
show('bad-float', lambda: math.isclose(BadFloat(), 1))
class CustomInt(int):
    def __float__(self): return 9.0
print(math.isclose(CustomInt(1), 9))
