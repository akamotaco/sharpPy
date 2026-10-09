data = bytearray(b'abcdef')
view = memoryview(data)
view[0] = ord('A')
assert data == bytearray(b'Abcdef')
assert view[0] is view[0]
data[1] = ord('B')
assert view[1] == ord('B')
part = view[1:5:2]
assert len(part) == 2
part[-1] = ord('D')
assert data == bytearray(b'ABcDef')
reverse = view[::-1]
reverse[0] = ord('F')
assert data == bytearray(b'ABcDeF')
nested = reverse[1:5:2]
nested[1] = ord('C')
assert data == bytearray(b'ABCDeF')
data[0:2] = b'xy'
assert view[0] == ord('x') and view[1] == ord('y')
data[::2] = b'123'
assert view[0] == 49 and view[2] == 50 and view[4] == 51
try:
    data.append(0)
except BufferError:
    pass
else:
    raise AssertionError('resized an exported buffer')
try:
    data[0:1] = b'longer'
except BufferError:
    pass
else:
    raise AssertionError('resized an exported buffer through a slice')
assert len(view[5:1]) == 0
readonly = memoryview(b'abc')[::-1]
assert readonly[0] == ord('c')
try:
    readonly[0] = 65
except TypeError:
    pass
else:
    raise AssertionError('read-only slice accepted a write')

# Slice bounds/step must reach bytearray storage intact.
plain = bytearray(b'abcdef')
assert plain[:2] == bytearray(b'ab')
assert plain[::-2] == bytearray(b'fdb')
plain[:] = b'uvwxyz'
plain[1::2] = b'ABC'
assert plain == bytearray(b'uAwByC')
del plain[::2]
assert plain == bytearray(b'ABC')
try:
    plain.__delitem__()
except TypeError:
    pass
else:
    raise AssertionError('missing deletion index accepted')
print('memoryview aliases: PASS')
