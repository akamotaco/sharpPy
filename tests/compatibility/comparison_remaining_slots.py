"""Pending: other native slots and builtin comparison descriptors."""
class Answer:
    def __eq__(self, other):
        return 'reflected'

print('string reflection', 'value' == Answer())

class Sequence(tuple):
    def __eq__(self, other):
        return 'tuple override'

print('tuple subclass', Sequence((1,)) == (1,))

try:
    print('int descriptor', int.__eq__(1, 1.0) is NotImplemented)
except AttributeError:
    print('int descriptor missing')
