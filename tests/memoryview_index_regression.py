def expect_error(kind, message, operation):
    try:
        operation()
    except kind as error:
        assert str(error) == message
    else:
        raise AssertionError('expected an exception')

data = bytearray(b'abc')
view = memoryview(data)
assert view[True] == ord('b') and view[False] == ord('a')

class Index:
    def __index__(self):
        return -1
    def __getattribute__(self, name):
        raise AssertionError('special lookup used instance attributes')

assert view[Index()] == ord('c')
view[Index()] = True
assert data == bytearray(b'ab\x01')

class Integer(int):
    def __index__(self):
        raise AssertionError('int subclass override must be bypassed')

assert view[Integer(1)] == ord('b')
view[Integer(1)] = Integer(65)
assert data == bytearray(b'aA\x01')

class InstanceOnly:
    pass

instance_only = InstanceOnly()
instance_only.__index__ = lambda: 0
expect_error(TypeError, 'memoryview: invalid slice key', lambda: view[instance_only])

class BadIndex:
    def __index__(self):
        return 'wrong'

class FailedIndex:
    def __index__(self):
        raise RuntimeError('index failed')

class HugeIndex:
    def __index__(self):
        return 1 << 100

expect_error(TypeError, '__index__ returned non-int (type str)', lambda: view[BadIndex()])
expect_error(RuntimeError, 'index failed', lambda: view[FailedIndex()])
expect_error(IndexError, "cannot fit 'HugeIndex' into an index-sized integer", lambda: view[HugeIndex()])

def assign(value):
    view[0] = value

for value in ('a', 1.5, BadIndex()):
    expect_error(TypeError, "memoryview: invalid type for format 'B'", lambda: assign(value))
for value in (-1, 256, 1 << 100):
    expect_error(ValueError, "memoryview: invalid value for format 'B'", lambda: assign(value))
expect_error(RuntimeError, 'index failed', lambda: assign(FailedIndex()))

class ValueFailure:
    def __index__(self):
        raise ValueError('underlying value error')

expect_error(ValueError, "memoryview: invalid value for format 'B'", lambda: assign(ValueFailure()))
# The shared index protocol is also used for slice components.
expect_error(RuntimeError, 'index failed', lambda: view[FailedIndex():])
expect_error(TypeError, '__index__ returned non-int (type str)', lambda: view[BadIndex():])

class ReleaseIndex:
    def __index__(self):
        view.release()
        return 0

expect_error(ValueError, 'operation forbidden on released memoryview object', lambda: view[ReleaseIndex()])
data.append(0)
view = memoryview(data)
expect_error(ValueError, 'operation forbidden on released memoryview object', lambda: assign(ReleaseIndex()))
data.append(0)
view = memoryview(data)
child = view[ReleaseIndex():]
assert child[0] == data[0]
expect_error(BufferError, 'Existing exports of data: object cannot be re-sized', lambda: data.append(0))
child.release()
data.append(0)

class ReleaseThenFail:
    def __index__(self):
        view.release()
        raise RuntimeError('slice failed')

view = memoryview(data)
expect_error(RuntimeError, 'slice failed', lambda: view[ReleaseThenFail():])
data.append(0)

class ReleaseValue:
    def __index__(self):
        view.release()
        return result

for result, message in ((-1, "memoryview: invalid value for format 'B'"),
                        (256, 'operation forbidden on released memoryview object'),
                        (1 << 100, "memoryview: invalid value for format 'B'")):
    view = memoryview(data)
    expect_error(ValueError, message, lambda: assign(ReleaseValue()))
    data.append(0)
print('memoryview index: PASS')
