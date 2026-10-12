"""Native-width hashes, identity, callback order, reentry and frozen hashes."""
for factory in (set, frozenset):
    for values in ([1, 1.0, True], [0, -0.0, False], [2**61, float(2**61)],
                   [(1, 2), (1.0, 2.0)], [float('inf'), float('inf')]):
        result = factory(values)
        print(factory.__name__, len(result), all(x in result for x in values))
    nan = float('nan')
    result = factory([nan, nan])
    print('nan', len(result), nan in result)

events = []
class Key:
    def __init__(self, name, code): self.name, self.code = name, code
    def __hash__(self):
        events.append(('hash', self.name))
        return self.code
    def __eq__(self, other):
        events.append(('eq', self.name, other.name))
        return self.name == other.name
for factory in (set, frozenset):
    a, b, c = Key('a', 7), Key('b', 7 + 2**32), Key('c', 7)
    events.clear()
    target = factory([a, b, c])
    print('construct', factory.__name__, events)
    events.clear()
    print('lookup', a in target, Key('a', 7) in target, events)
    events.clear()
    copied = factory(target)
    print('copy', len(copied), events)
    if factory is frozenset:
        events.clear()
        print('hash frozen', hash(target), hash(target), events)
    else:
        events.clear()
        target.remove(a)
        print('remove', len(target), events)
        events.clear()
        target.pop()
        print('pop', len(target), events)

for values in ([], [1], [1,2,3], [2**80, -2**80], [1.5, float('inf')]):
    print('frozen', hash(frozenset(values)))
print('nested', hash(frozenset([frozenset([1,2]), frozenset([3])])) )

for operation in ('contains', 'add', 'remove'):
    for equal in (False, True):
        target = set()
        class Mutating:
            def __hash__(self): return 42
            def __eq__(self, other):
                target.clear()
                return equal
        target.add(Mutating())
        try:
            if operation == 'contains': result = 42 in target
            elif operation == 'add': result = target.add(42)
            else: result = target.remove(42)
            print('reenter', operation, equal, result, len(target))
        except KeyError:
            print('reenter', operation, equal, 'KeyError', len(target))

class Broken:
    def __hash__(self): return 8
    def __eq__(self, other): raise ValueError('equality')
key = Broken(); target = {key}
print('identity', key in target)
try: Broken() in target
except ValueError as error: print('error', str(error))

for operation in ('union', 'intersection', 'difference', 'symmetric_difference',
                  'issubset', 'issuperset', 'isdisjoint', 'update'):
    left = {Key('a', 10), Key('b', 20)}
    right = {Key('b', 20), Key('c', 30)}
    events.clear()
    result = getattr(left, operation)(right)
    print('algebra', operation, sorted(x.name for x in result) if isinstance(result, set) else result, events)

events.clear()
def source():
    events.append('next-a')
    yield Key('a', 1)
    events.append('next-b')
    yield Key('b', 2)
print('stream', len(set(source())), events)
values = set(range(600))
for i in range(0, 600, 3): values.remove(i)
for i in range(600, 1500): values.add(i)
print('resize', len(values), sum(values), all(i in values for i in range(600, 1500)))
