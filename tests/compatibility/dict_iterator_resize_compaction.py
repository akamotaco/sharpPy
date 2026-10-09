"""Known gap: CPython entry compaction changes an active numeric cursor."""
d = {0: 0, 1: 1, 2: 2, 3: 3, 4: 4}
iterator = iter(d)
assert next(iterator) == 0
del d[0]
d[5] = 5
# This insertion compacts the five-entry CPython 3.12 table.
assert next(iterator) == 2
print('dict iterator resize compaction: PASS')
