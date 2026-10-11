events = []
class Key:
    def __init__(self, name, code): self.name, self.code = name, code
    def __hash__(self):
        events.append(('hash', self.name))
        return self.code
    def __eq__(self, other):
        events.append(('eq', self.name, other.name))
        return self.name == other.name
    def __repr__(self): return 'K' + self.name

a = Key('a', 7)
b = Key('b', 7)
c = Key('c', 7 + 2**32)
d = {}
for key in (a, b, c):
    events.clear()
    d[key] = key.name
    print('insert', events)
events.clear()
print('view', d, list(d.items()), list(d.values()), events)
events.clear()
print('iterate', [pair for pair in d.items()], [value for value in d.values()], events)
events.clear()
print('item contains', (Key('b', 7), 'b') in d.items(), events)
for key in (a, Key('b', 7), Key('missing', 7), c):
    events.clear()
    print('get', d.get(key), events)
events.clear()
d[b] = 'updated'
print('update', events)
events.clear()
print('default', d.setdefault(b, 'no'), events)
events.clear()
print('default new', d.setdefault(Key('new', 7), 'yes'), events)
events.clear()
print('pop', d.pop(Key('b', 7)), events)
events.clear()
del d[c]
print('delete', events)
events.clear()
copy = d.copy()
print('copy', copy, events)
events.clear()
copy = dict(d)
print('construct', copy, events)
events.clear()
copy = {}
copy.update(d)
print('merge', copy, events)
events.clear()
print('popitem', copy.popitem(), events)
events.clear()
print('equal', d == d.copy(), events)

for count in (2, 6, 20):
    source = {Key(str(i), 1): i for i in range(count)}
    events.clear()
    target = {}
    target.update(source)
    print('dense merge', count, len(events), len(target))
    for i in range(count - 1): del source[Key(str(i), 1)]
    events.clear()
    target = source.copy()
    print('sparse copy', count, events, list(target.values()))

for mode in ('clear', 'delete', 'grow', 'value'):
    events.clear()
    mapping = {}
    active = [True]
    class Mutating:
        def __hash__(self): return 42
        def __eq__(self, other):
            events.append('eq')
            if active[0]:
                active[0] = False
                if mode == 'clear': mapping.clear()
                elif mode == 'delete': del mapping[self]
                elif mode == 'grow':
                    for i in range(100): mapping[i] = i
                else: mapping[self] = 'changed'
            return True
    key = Mutating()
    mapping[key] = 'original'
    print('reenter', mode, mapping.get(Mutating(), 'missing'), events, len(mapping))

for value in (0.0, -0.0, 1.0, -1.0, 0.5, -0.125, 1.5, 2.0**100, 2.0**-100,
              float('inf'), -float('inf'), 5e-324):
    print('float hash', hash(value))
print('tuple hashes', hash((1, 2.0)), hash((1.0, 2)), {(2**100,): 'yes'}.get((2.0**100,)))

class Invalid:
    def __hash__(self): return 'bad'
try: {Invalid(): 1}
except TypeError as error: print(str(error))
events.clear()
print('empty pop', {}.pop(Key('empty', 1), 'default'), events)

for operation in ('get', 'set', 'pop', 'default'):
    mapping = {}
    active = [True]
    class Replacing:
        def __hash__(self): return 1
        def __eq__(self, other):
            if active[0]:
                active[0] = False
                mapping.clear()
                mapping[other] = 'replacement'
            return True
    mapping[Replacing()] = 'original'
    probe = Replacing()
    if operation == 'get': result = mapping.get(probe)
    elif operation == 'set':
        mapping[probe] = 'set'
        result = mapping[probe]
    elif operation == 'pop': result = mapping.pop(probe)
    else: result = mapping.setdefault(probe, 'default')
    print('replace', operation, result, len(mapping))

# Cross the compact index table boundary, then copy and mutate both tables.
large = {i: i for i in range(50000)}
wide_copy = large.copy()
del wide_copy[43690]
wide_copy[-1] = 7
print('wide', len(large), len(wide_copy), large[43690], wide_copy.get(43690), wide_copy[-1], large[49999])
for code in (-2**63, -2, 2**63 - 1):
    events.clear()
    original = Key('wide hash', code)
    values = {original: 'value'}
    print('signed hash', code, values[Key('wide hash', code)], events)
class Unhashable:
    def __eq__(self, other): return True
try: {Unhashable(): 1}
except TypeError as error: print(str(error))
