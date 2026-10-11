"""Containers resolve both operands and truth-test comparison results."""
events = []

class Truth:
    def __bool__(self):
        events.append('bool')
        return True

class Key(int):
    def __eq__(self, other):
        events.append('key.eq')
        return Truth()

    __hash__ = int.__hash__

class Child(Key):
    def __eq__(self, other):
        events.append('child.eq')
        return Truth()

    __hash__ = int.__hash__

key = Key(1)
child = Child(1)

def show(label, operation):
    events.clear()
    print(label, operation(), events)

show('list membership', lambda: child in [key])
show('list equality', lambda: [key] == [child])
show('tuple membership', lambda: child in (key,))
show('tuple equality', lambda: (key,) == (child,))
mapping = {key: 'value'}
show('dict lookup', lambda: mapping[child])
show('dict values', lambda: {'key': key} == {'key': child})
show('view values', lambda: child in {'key': key}.values())
show('set membership', lambda: child in {key})
show('list identity', lambda: key in [key])
show('tuple identity', lambda: key in (key,))
show('dict identity', lambda: mapping[key])
show('set identity', lambda: key in {key})

class Explosive:
    def __eq__(self, other):
        raise ValueError('comparison failure')

try:
    object() in [Explosive()]
except ValueError as error:
    print('membership error', str(error))
else:
    raise AssertionError('comparison exception was swallowed')

class Ordered:
    def __eq__(self, other):
        events.append('eq')
        return False

    def __lt__(self, other):
        events.append('lt')
        return marker

marker = Truth()
left = Ordered()
right = Ordered()
show('tuple ordering result', lambda: ((left,) < (right,)) is marker)
show('list ordering result', lambda: ([left] < [right]) is marker)
show('tuple unequal length', lambda: (left,) == (right, 1))
show('minimum', lambda: min(left, right) is right)
