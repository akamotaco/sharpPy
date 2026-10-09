values = [b'', b'a', b'aa', b'ab', b'b', b'\x00', b'\x7f', b'\x80', b'\xff', b'a\x00']
for left in values:
    for right in values:
        a, b = list(left), list(right)
        assert (left == right) == (a == b)
        assert (left != right) == (a != b)
        assert (left < right) == (a < b)
        assert (left <= right) == (a <= b)
        assert (left > right) == (a > b)
        assert (left >= right) == (a >= b)
        mutable = bytearray(right)
        assert (left == mutable) == (a == b)
        assert (left != mutable) == (a != b)
        assert (left < mutable) == (a < b)
        assert (left <= mutable) == (a <= b)
        assert (left > mutable) == (a > b)
        assert (left >= mutable) == (a >= b)

assert b'abc'[:] == b'abc'
assert [b'abc'[:]] == [b'abc']
assert (b'abc'[:],) == (b'abc',)
assert b'abc'[:] in [b'abc']
assert {b'abc': 7}[b'abc'[:]] == 7
assert hash(b'abc'[:]) == hash(b'abc')
assert len({b'abc', b'abc'[:]}) == 1
for other in ['abc', 1, None, [], {}]:
    assert not (b'abc' == other)
    assert b'abc' != other

calls = []
class Reflected:
    def __eq__(self, other):
        calls.append('eq')
        return 'equal'
    def __ne__(self, other):
        calls.append('ne')
        return 'unequal'
    def __gt__(self, other):
        calls.append('gt')
        return 'greater'
other = Reflected()
assert (b'abc' == other) == 'equal'
assert (b'abc' != other) == 'unequal'
assert (b'abc' < other) == 'greater'
assert calls == ['eq', 'ne', 'gt']
class Declined:
    def __eq__(self, other):
        return NotImplemented
    def __ne__(self, other):
        return NotImplemented
    def __gt__(self, other):
        return NotImplemented
other = Declined()
assert not (b'abc' == other)
assert b'abc' != other
assert other == other
assert not (other != other)
try:
    b'abc' < other
except TypeError as error:
    assert str(error) == "'<' not supported between instances of 'bytes' and 'Declined'"
else:
    raise AssertionError('expected TypeError')
print('bytes comparisons: PASS')
