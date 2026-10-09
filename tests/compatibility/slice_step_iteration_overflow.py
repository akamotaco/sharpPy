# Separate from conversion overflow: step fits int, but start + step does not.
assert list(b'abcdef'[1::2147483647]) == [98]
print('slice step iteration overflow: PASS')
