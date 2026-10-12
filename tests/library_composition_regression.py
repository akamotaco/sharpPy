"""Cross-module behavior: iteration, callbacks, sorting, numeric keys, reductions."""
import math
import itertools
from functools import reduce
from operator import add, mul, itemgetter

values = list(itertools.chain(range(4), (5, 6), [7]))
assert values == [0, 1, 2, 3, 5, 6, 7]
print('chain', values)
print('islice', list(itertools.islice(iter(values), 1, 6, 2)))
print('accumulate', list(itertools.accumulate([1, 2, 3, 4], add)))
print('reduce', reduce(mul, [2, 3, 4], 1))
print('math', math.gcd(84, 30), math.sqrt(81) == 9.0)

rows = [('b', 2), ('a', 1), ('c', 2), ('d', 1)]
ordered = sorted(rows, key=itemgetter(1))
assert ordered == [('a', 1), ('d', 1), ('b', 2), ('c', 2)]
print('stable', ordered)
print('numeric', len(set([1, 1.0, True])), {1: 'first', 1.0: 'last'}[True])

events = []
def source():
    for value in range(4):
        events.append(value)
        yield value
iterator = itertools.islice(source(), 2)
print('lazy-before', events)
print('lazy-first', next(iterator), events)
print('lazy-rest', list(iterator), events)

class Row:
    def __init__(self, value): self.value = value
    def __lt__(self, other): return self.value < other.value
print('objects', [row.value for row in sorted([Row(3), Row(1), Row(2)])])
