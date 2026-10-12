"""Shared small tables and equality reentry across resize and new set creation."""
for count in (0, 1, 4, 5, 8, 17):
    original = set(range(count))
    saved = original.copy()
    for index in range(40):
        original.add(100 + index)
        transient = set(range(5))
        assert transient == {0, 1, 2, 3, 4}
    assert saved == set(range(count))
    original.clear()
    assert saved == set(range(count))

events = []
armed = False
class Key:
    def __init__(self, value): self.value = value
    def __hash__(self): return 0
    def __eq__(self, other):
        global armed
        events.append((self.value, other.value))
        if armed:
            armed = False
            owner.update(range(10, 50))
            for index in range(3):
                scratch = set(range(5))
                assert scratch == {0, 1, 2, 3, 4}
        return self.value == other.value
owner = {Key(0), Key(1)}
events.clear()
armed = True
print('lookup', Key(2) in owner, events, len(owner))
print('small table lifetime: PASS')
for size in (4, 5, 17, 65):
    original = set(range(size))
    original.pop()
    copied = original.copy()
    print('pop after sharing', size, [original.pop() for _ in range(3)],
          [copied.pop() for _ in range(3)])
    copied.clear()
    copied.add(42)
    print('pop after clear', copied.pop(), len(copied), len(original))
