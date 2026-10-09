# Keep traversal bounded even when a broken iterator would never terminate.
lo = -(2 ** 63)
hi = 2 ** 63 - 1
cases = [
    (hi - 1, hi, 2, [hi - 1]),
    (lo + 1, lo, -2, [lo + 1]),
    (0, hi, hi, [0]),
    (0, lo, lo, [0]),
    (lo, hi, hi, [lo, -1, hi - 1]),
    (hi, lo, lo, [hi, -1]),
    (lo, hi, 2 ** 62, [lo, -(2 ** 62), 0, 2 ** 62]),
    (hi, lo, -(2 ** 62), [hi, 2 ** 62 - 1, -1, -(2 ** 62) - 1]),
    (hi, lo, 1, []),
    (lo, hi, -1, []),
]
for start, stop, step, expected in cases:
    r = range(start, stop, step)
    it = iter(r)
    for value in expected:
        assert next(it) == value
    for attempt in range(2):
        try:
            next(it)
            assert False, 'iterator did not stay exhausted'
        except StopIteration:
            pass
    assert len(r) == len(expected)
    assert bool(r) == bool(expected)
    assert list(r) == expected
    assert tuple(r) == tuple(expected)
    for i in range(len(expected)):
        assert r[i] == expected[i]
        assert r[i - len(expected)] == expected[i]
    for i in (len(expected), -len(expected) - 1):
        try:
            r[i]
            assert False, 'missing IndexError'
        except IndexError:
            pass

# A valid range may have more than long.MaxValue items. Do not materialize it.
r = range(lo, hi)
assert bool(r)
assert r[0] == lo
assert r[hi] == -1
assert r[-1] == hi - 1
assert r[lo] == -1
it = iter(r)
assert next(it) == lo
assert next(it) == lo + 1

# Cross the signed boundaries in both directions, without unbounded traversal.
checked = 0
for start in (lo, lo + 1, -1, 0, 1, hi - 1, hi):
    for stop in (lo, lo + 1, -1, 0, 1, hi - 1, hi):
        for step in (lo, -hi, -2, -1, 1, 2, hi):
            r = range(start, stop, step)
            size = max(0, (stop - start + step - 1) // step) if step > 0 else max(0, (start - stop - step - 1) // (-step))
            assert bool(r) == (size != 0)
            if size <= 12:
                expected = [start + i * step for i in range(size)]
                it = iter(r)
                for value in expected:
                    assert next(it) == value
                try:
                    next(it)
                    assert False, 'boundary iterator did not stop'
                except StopIteration:
                    pass
                assert len(r) == size
                assert list(r) == expected
            else:
                assert r[0] == start
                assert r[-1] == start + (size - 1) * step
            checked += 1
assert checked == 343
# bytes(range) uses SharpPy's separate GetValues path.
assert bytes(range(1, hi, hi)) == b'\x01'
assert bytes(range(0, hi, hi)) == b'\x00'
print('range native boundary: PASS')
