"""Numeric fast paths preserve callbacks, reflection and dynamic slot changes."""

events = []

class Number(int):
    pass

class Child(Number):
    pass

operations = (
    ('__lt__', lambda a, b: a < b),
    ('__le__', lambda a, b: a <= b),
    ('__eq__', lambda a, b: a == b),
    ('__ne__', lambda a, b: a != b),
    ('__gt__', lambda a, b: a > b),
    ('__ge__', lambda a, b: a >= b),
)

def callback(name, mode):
    def compare(self, other):
        events.append(name)
        if mode == 'decline':
            return NotImplemented
        if mode == 'error':
            raise ValueError(name)
        return name
    return compare

def show(label, operation, left, right):
    events.clear()
    try:
        result = operation(left, right)
        print(label, result, events)
    except ValueError as error:
        print(label, 'ValueError', str(error), events)

number = Child(1)
for name, operation in operations:
    for other in (0, 1, 2, False, True, 0.5, 1.0, 1.5):
        # Prime the inherited slot before editing its parent class.
        show(('inherited', name, type(other).__name__, other), operation, number, other)
    for mode in ('value', 'decline', 'error'):
        setattr(Number, name, callback(name, mode))
        for other in (1, True, 1.0):
            show((mode, name, 'left', type(other).__name__), operation, number, other)
            show((mode, name, 'right', type(other).__name__), operation, other, number)
    delattr(Number, name)
    show(('deleted', name), operation, number, 1)
