"""Compare sort callbacks, errors, stability and retained list permutations."""
events = []
limit = -1
class Truth:
    def __init__(self, value): self.value = value
    def __bool__(self):
        events.append(('bool', self.value))
        return self.value
class Item:
    def __init__(self, value, index): self.value, self.index = value, index
    def __lt__(self, other):
        events.append((self.index, other.index))
        if len(events) == limit: raise ValueError('comparison failed')
        return Truth(self.value < other.value)
    def __gt__(self, other): raise RuntimeError('must use lt')

for size in (0, 1, 2, 17, 63, 64, 65, 127, 257, 1024):
    for pattern in range(4):
        for reverse in (False, True):
            events.clear()
            source = [Item((i * 73 + 19) % 101 if pattern == 0 else
                           (size - i if pattern == 1 else (i % 7 if pattern == 2 else i)), i)
                      for i in range(size)]
            source.sort(reverse=reverse)
            print(size, pattern, reverse, [x.index for x in source], events)

for limit in (1, 3, 15, 101, 301, 501, 1801, 3501):
    for reverse in (False, True):
        events.clear()
        source = [Item((i * 73 + 19) % 101, i) for i in range(511)]
        try: source.sort(reverse=reverse)
        except ValueError as error: print(str(error))
        print('error state', limit, reverse, [x.index for x in source], events)
limit = -1
events.clear()
print('sorted', [x.index for x in sorted([Item(3, 0), Item(1, 1), Item(1, 2)])], events)

for reverse in (False, True):
    source = [(2, 'a'), (1, 'b'), (2, 'c'), (1, 'd')]
    def key(x):
        print('key', x, list(source))
        return x[0]
    source.sort(key=key, reverse=reverse)
    print('stable', source)

for fail in (False, True):
    source = [3, 1, 2]
    def key(x):
        print('mutate key', x, list(source))
        source.append(99)
        if fail and x == 1: raise RuntimeError('key failed')
        return x
    try: source.sort(key=key)
    except (ValueError, RuntimeError) as error: print(type(error).__name__, str(error))
    print('restored', source)

class Reverse:
    def __bool__(self):
        print('reverse bool')
        return True
print('reverse', sorted([1, 3, 2], reverse=Reverse()))

for mode in range(3):
    source = [3, 1, 2]
    def key(x):
        if mode == 0:
            source.append(9)
            source.pop()
        elif mode == 1:
            source.sort()
        else:
            source.clear()
        return x
    try: source.sort(key=key)
    except ValueError as error: print(str(error))
    print('mutation', mode, source)

for name in ('sort', 'sorted'):
    try:
        if name == 'sort': [1].sort(unknown=True)
        else: sorted([1], unknown=True)
    except TypeError as error: print(name, str(error))
