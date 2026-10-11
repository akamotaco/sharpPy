"""A stored key is found by identity before invoking equality."""

class Explosive:
    def __hash__(self):
        return 42

    def __eq__(self, other):
        raise AssertionError('identity must bypass equality')


def check(key):
    mapping = {key: 1}
    assert key in mapping
    assert mapping[key] == 1
    assert mapping.get(key) == 1
    assert mapping.setdefault(key, 99) == 1
    mapping[key] = 2
    assert len(mapping) == 1
    assert list(mapping.values()) == [2]
    assert list(mapping.items())[0][0] is key
    copied = mapping.copy()
    assert copied[key] == 2
    mapping.update(copied)
    assert len(mapping) == 1
    assert mapping.pop(key) == 2
    assert len(mapping) == 0
    mapping[key] = 3
    del mapping[key]
    assert len(mapping) == 0
    mapping[key] = 4
    item = mapping.popitem()
    assert item[0] is key and item[1] == 4
    assert len(mapping) == 0


check(Explosive())
check(float('nan'))
check(1000)
check('identity key')

# Distinct keys must still use equality.
first = float('nan')
second = float('nan')
mapping = {first: 1, second: 2}
assert len(mapping) == 2
assert mapping[first] == 1 and mapping[second] == 2

class Observed:
    calls = 0

    def __hash__(self):
        return 17

    def __eq__(self, other):
        Observed.calls += 1
        return False

key = Observed()
check(key)
assert Observed.calls == 0
print('dict key identity: ok')
