"""Stored hashes, detached copies, collisions, and reentrant equality events."""
for count in (0, 1, 4, 5, 16, 64, 200):
    original = set(range(count))
    a = original.copy()
    b = a.copy()
    original.add(-1)
    a.add(-2)
    b.add(-3)
    assert original == set(range(count)) | {-1}
    assert a == set(range(count)) | {-2}
    assert b == set(range(count)) | {-3}
    c = a.copy()
    a.clear()
    assert c == set(range(count)) | {-2}
    for operation in ('update', 'intersection_update', 'difference_update', 'symmetric_difference_update'):
        left = c.copy()
        saved = left.copy()
        getattr(left, operation)({-2, 7})
        assert saved == c
    print('copies', count, len(original), len(b), len(c))

events = []
target = set()
class Key:
    def __init__(self, number): self.number = number
    def __hash__(self): return 42
    def __eq__(self, other):
        events.append((self.number, other.number))
        if self.number == 0 and other.number == 99:
            target.add(1000)
        return self.number == other.number

for shared in (False, True):
    target = {Key(0), Key(1), Key(2)}
    if shared:
        saved = target.copy()
    events.clear()
    print('reentry copy', shared, Key(99) in target, events)
    print('values', sorted(x.number for x in target if isinstance(x, Key)))
    if shared: assert 1000 not in saved

source = set(range(120))
for step in range(200):
    key = (step * 37) % 251
    snapshot = source.copy()
    if step % 3 == 0: source.add(key)
    elif step % 3 == 1: source.discard(key)
    else: source.symmetric_difference_update({key})
    assert snapshot is not source
print('mutations', len(source), sum(source), sorted(source)[:12])
print('set copy and reentry: PASS')

for operation in ('union', 'intersection', 'difference', 'issubset', 'issuperset', 'isdisjoint', 'update'):
    events.clear()
    def source():
        for number in (1, 2, 9):
            events.append(number)
            yield number
    target = {1, 2}
    result = getattr(target, operation)(source())
    print('consume', operation, sorted(result) if isinstance(result, set) else result, events)
