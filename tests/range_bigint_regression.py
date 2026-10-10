import sys
import _operator

huge = 10 ** 100
cases = [
    (0, huge, huge, [0]),
    (1, huge, huge, [1]),
    (huge, huge + 7, 2, [huge, huge + 2, huge + 4, huge + 6]),
    (-huge, -huge - 7, -2, [-huge, -huge - 2, -huge - 4, -huge - 6]),
    (-huge, huge, huge, [-huge, 0]),
    (huge, -huge, -huge, [huge, 0]),
    (huge, 0, 1, []),
    (-huge, 0, -1, []),
]
for start, stop, step, expected in cases:
    r = range(start, stop, step)
    assert type(r) is type(range(0))
    assert (r.start, r.stop, r.step) == (start, stop, step)
    expected_repr = 'range(' + str(start) + ', ' + str(stop)
    if step != 1:
        expected_repr += ', ' + str(step)
    assert repr(r) == expected_repr + ')'
    assert bool(r) == bool(expected)
    assert len(r) == len(expected)
    assert _operator.length_hint(r) == len(expected)
    assert list(r) == expected
    assert tuple(r) == tuple(expected)
    it = iter(r)
    for value in expected:
        assert next(it) == value
    for attempt in range(2):
        try:
            next(it)
            assert False, 'iterator failed to exhaust'
        except StopIteration:
            pass
    for i in range(len(expected)):
        assert r[i] == expected[i]
        assert r[i - len(expected)] == expected[i]
    for stride in (1, -1, 2, -2, huge, -huge):
        assert list(r[::stride]) == expected[::stride]
        assert list(r[::stride][::-1]) == expected[::stride][::-1]

r = range(-huge, huge)
assert bool(r)
assert r[huge] == 0
assert r[-huge] == 0
assert list(r[huge:huge + 3]) == [0, 1, 2]
assert list(r[:3]) == [-huge, -huge + 1, -huge + 2]
for length_function in (len, _operator.length_hint):
    try:
        length_function(r)
        assert False, 'expected length overflow'
    except OverflowError as error:
        assert str(error) == 'Python int too large to convert to C ssize_t'
assert len(range(sys.maxsize)) == sys.maxsize
assert bytes(range(1, huge, huge)) == b'\x01'
assert repr(range(3)) == 'range(0, 3)'

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value

r = range(Index('start', huge), Index('stop', huge + 3), Index('step', 1))
assert calls == ['start', 'stop', 'step']
calls.clear()
assert r[Index('index', 1)] == huge + 1
assert calls == ['index']
calls.clear()
assert list(r[Index('start', 0):Index('stop', 2):Index('step', huge)]) == [huge]
assert calls == ['step', 'start', 'stop']
assert list(range(False, True, True)) == [0]

# Mapping a native range slice may overflow even when each selected item fits.
lo = -(2 ** 63)
hi = 2 ** 63 - 1
calls.clear()
r = range(lo, hi, hi)
s = r[Index('start', 0):Index('stop', 3):2]
assert calls == ['start', 'stop']
assert (s.start, s.stop, s.step) == (lo, lo + 3 * hi, 2 * hi)
assert list(s) == [lo, hi - 1]
assert list(s[::-1]) == [hi - 1, lo]

for boundary in (lo - 1, lo, hi, hi + 1):
    r = range(boundary, boundary + 2)
    assert list(r) == [boundary, boundary + 1]
    assert list(r[::-1]) == [boundary + 1, boundary]

def expect_error(action, exception, message):
    try:
        action()
    except exception as error:
        assert str(error) == message
    else:
        raise AssertionError('expected exception')

expect_error(lambda: range(), TypeError, 'range expected at least 1 argument, got 0')
expect_error(lambda: range(1, 2, 3, 4), TypeError, 'range expected at most 3 arguments, got 4')
expect_error(lambda: range(stop=3), TypeError, 'range() takes no keyword arguments')
expect_error(lambda: range('3'), TypeError, "'str' object cannot be interpreted as an integer")
expect_error(lambda: range(huge, huge + 1, 0), ValueError, 'range() arg 3 must not be zero')
expect_error(lambda: range(3)[huge], IndexError, 'range object index out of range')
expect_error(lambda: range(huge)[huge], IndexError, 'range object index out of range')
expect_error(lambda: range(3)[1.5], TypeError, 'range indices must be integers or slices, not float')
calls.clear()
expect_error(lambda: range(Index('start', huge), Index('stop', huge + 1), Index('step', 0)),
             ValueError, 'range() arg 3 must not be zero')
assert calls == ['start', 'stop', 'step']
print('range bigint: PASS')

# Constructor conversion must stop at the first failure, including the
# less common bool / __index__ paths used alongside ordinary int arguments.
class BadIndex:
    def __index__(self):
        calls.append('bad')
        return 'bad'

for args, expected_calls in (
    ((BadIndex(),), ['bad']),
    ((BadIndex(), Index('stop', 4)), ['bad']),
    ((Index('start', 0), BadIndex(), Index('step', 1)), ['start', 'bad']),
    ((Index('start', 0), Index('stop', 4), BadIndex()), ['start', 'stop', 'bad']),
):
    calls.clear()
    expect_error(lambda: range(*args), TypeError, '__index__ returned non-int (type str)')
    assert calls == expected_calls
calls.clear()
assert list(range(Index('stop', 3))) == [0, 1, 2]
assert calls == ['stop']
assert list(range(True)) == [0]
assert list(range(False, True)) == [0]
expect_error(lambda: range(False, True, False), ValueError, 'range() arg 3 must not be zero')
print('range constructor conversion: PASS')
