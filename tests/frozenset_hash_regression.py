events = []
class Key:
    def __init__(self, value): self.value = value
    def __hash__(self):
        events.append(('hash', self.value))
        return self.value
    def __eq__(self, other):
        events.append(('eq', self.value, other.value))
        return self.value == other.value

value = frozenset([Key(7), Key(7 + 2**32)])
print('construct', events)
events.clear()
print('hash', hash(value), hash(value), events)
print('identity', value.copy() is value, frozenset(value) is value)
for values in ([], [1], [1, 2, 3], [2**80, -2**80], [1.5, float('inf')]):
    print(hash(frozenset(values)))
print(hash(frozenset([frozenset([1, 2]), frozenset([3])])))

for factory in (set, frozenset):
    events.clear()
    def source():
        events.append('next-a')
        yield Key(1)
        events.append('next-b')
        yield Key(2)
    print(factory.__name__, len(factory(source())), events)
