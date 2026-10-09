bounds = [None, -100, -1, 0, 1, 100]
steps = [1, 2, 3, -1, -2, -3, 2147483647, -2147483648]
cases = 0
for length in [0, 1, 2, 7, 33]:
    original = list(range(length))
    for start in bounds:
        for stop in bounds:
            for step in steps:
                key = slice(start, stop, step)
                removed = original[key]
                expected = [value for value in original if value not in removed]
                items = original[:]
                alias = items
                del items[key]
                assert items == expected
                assert alias is items and alias == expected
                items.append(99)
                assert items == expected + [99]
                data = bytearray(original)
                del data[key]
                assert list(data) == expected
                data.append(99)
                assert list(data) == expected + [99]
                data = bytearray(original)
                data[key] = b''
                assert list(data) == expected
                cases += 1

class Token:
    pass
tokens = [Token() for i in range(9)]
items = tokens[:]
del items[7:0:-3]
expected = [tokens[i] for i in [0, 2, 3, 5, 6, 8]]
assert len(items) == len(expected)
for i in range(len(items)):
    assert items[i] is expected[i]
print('slice deletion compaction:', cases, 'PASS')
