"""Shallow copies preserve references and Python order after slot reuse."""
for size in (0, 1, 5, 100):
    original = {i: [i] for i in range(size)}
    for i in range(0, size, 2):
        del original[i]
    original['last'] = [42]
    if size:
        original[0] = [43]
    expected = list(original)
    copied = original.copy()
    assert copied is not original
    assert list(copied) == expected
    assert list(copied.items()) == list(original.items())
    for key in expected:
        assert copied[key] is original[key]
    copied['last'].append(99)
    assert original['last'] == [42, 99]
    del copied['last']
    assert 'last' in original
    original.clear()
    assert list(copied) == [key for key in expected if key != 'last']
    while copied:
        key, value = copied.popitem()
        assert key != 'last'

empty = {}
assert empty.copy() == {} and empty.copy() is not empty
mixed = {True: 'first', (1, 2): 'tuple', 'text': 'string'}
mixed[1] = 'updated'
copied = mixed.copy()
assert list(copied.items()) == [(True, 'updated'), ((1, 2), 'tuple'), ('text', 'string')]
assert list(copied)[0] is True

print('dict copy: PASS')
