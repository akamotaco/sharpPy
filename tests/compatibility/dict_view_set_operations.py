"""Known gap: dict keys/items support set equality and operations."""
d = {'first': 1}
assert d.keys() == {'first'}
assert d.items() == {('first', 1)}
assert d.keys() & {'first', 'second'} == {'first'}
print('dict view set operations: PASS')
