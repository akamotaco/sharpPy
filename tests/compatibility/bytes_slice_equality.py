result = b'abc'[:]
assert list(result) == [97, 98, 99]
assert result == b'abc'
assert not (result != b'abc')
print('bytes slice equality: PASS')
