def expect_error(kind, message, operation):
    try:
        operation()
    except kind as error:
        assert str(error) == message
    else:
        raise AssertionError('expected exception')

data = bytearray(b'abcdef')
view = memoryview(data)
view[1:4] = b'XYZ'
assert data == bytearray(b'aXYZef')
view[::2] = bytearray(b'123')
assert data == bytearray(b'1X2Z3f')
view[::-2] = b'ABC'
assert data == bytearray(b'1C2B3A')
source = view[:-1]
view[1:] = source
source.release()
assert data == bytearray(b'11C2B3')
source = view[::-1]
view[:] = source
source.release()
assert data == bytearray(b'3B2C11')
source = view[1::2]
view[::2] = source
source.release()
assert data == bytearray(b'BBCC11')
view[0:0] = b''

def assign(source):
    view[:] = source

before = data[:]
expect_error(ValueError, 'memoryview assignment: lvalue and rvalue have different structures', lambda: assign(b'x'))
assert data == before
expect_error(TypeError, "a bytes-like object is required, not 'list'", lambda: assign([1, 2]))
view.release()
data.append(0)  # source export acquired for bytearray/bytes must not leak

data = bytearray(b'abc')
view = memoryview(data)
other = bytearray(b'xyz')
view[:] = other
view.release()
other.append(0)
data.append(0)

view = memoryview(bytearray(b'abc'))
source = memoryview(b'xyz')
class ReleaseSource:
    def __index__(self):
        source.release()
        return 0

def guarded_assign():
    view[ReleaseSource():] = source

expect_error(BufferError, 'memoryview has 1 exported buffer', guarded_assign)
assert view[0] == ord('a')
source.release()  # failed assignment must return its temporary export
view.release()

data = bytearray(b'abc')
view = memoryview(data)
class ReleaseDestination:
    def __index__(self):
        view.release()
        return 0

def released_assign():
    view[ReleaseDestination():] = b'xyz'

expect_error(ValueError, 'operation forbidden on released memoryview object', released_assign)
assert data == bytearray(b'abc')
data.append(0)

view = memoryview(b'abc')
expect_error(TypeError, 'cannot modify read-only memory', lambda: assign(b'xyz'))
view.release()
# Exercise overlapping gathers/scatters against an independent bytearray result.
slices = [slice(None), slice(None, None, -1), slice(None, None, 2),
          slice(None, None, -2), slice(1, None, 2), slice(1, 5),
          slice(0, 4), slice(2, 2)]
for destination_slice in slices:
    for source_slice in slices:
        data = bytearray(b'abcdef')
        expected = data[:]
        replacement = expected[source_slice]
        if len(expected[destination_slice]) != len(replacement):
            continue
        expected[destination_slice] = replacement
        view = memoryview(data)
        source = view[source_slice]
        view[destination_slice] = source
        assert data == expected
        source.release()
        view.release()
        data.append(0)

data = bytearray(b'abc')
view = memoryview(data)
source = memoryview(b'xyz')
def zero_step():
    view[::0] = source
expect_error(ValueError, 'slice step cannot be zero', zero_step)
source.release()
expect_error(ValueError, 'operation forbidden on released memoryview object', lambda: assign(source))
view[:] = view
assert data == bytearray(b'abc')
view.release()
data.append(0)
print('memoryview slice assignment: PASS')
