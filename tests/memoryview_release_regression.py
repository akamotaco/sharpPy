def check_released(view):
    for operation in (lambda: len(view), lambda: bool(view), lambda: view[0],
                      lambda: view[:], lambda: view.itemsize,
                      lambda: memoryview(view), lambda: view.__enter__()):
        try:
            operation()
        except ValueError as error:
            assert str(error) == 'operation forbidden on released memoryview object'
        else:
            raise AssertionError('released view remained accessible')
    try:
        view[0] = 1
    except ValueError:
        pass
    else:
        raise AssertionError('released view accepted a write')
    assert 'released memory' in repr(view)
    assert view.release() is None

data = bytearray(b'abc')
view = memoryview(data)
child = view[1:]
copied = memoryview(child)
assert view.release() is None
assert view.release() is None
check_released(view)
child[0] = ord('B')
assert copied[0] == ord('B')
assert data == bytearray(b'aBc')
child.release()
try:
    data.append(0)
except BufferError:
    pass
else:
    raise AssertionError('live copied view did not protect exporter')
copied.release()
data.append(ord('d'))
assert data == bytearray(b'aBcd')

# Independent exports and empty slices also protect the exporter.
first = memoryview(data)
second = memoryview(data)
empty = first[0:0]
first.release()
second.release()
try:
    data.clear()
except BufferError:
    pass
else:
    raise AssertionError('empty live view did not protect exporter')
empty.release()
data.clear()
assert data == bytearray()

data = bytearray(b'x')
with memoryview(data) as context:
    context[0] = ord('y')
assert data == bytearray(b'y')
check_released(context)
data.append(ord('z'))
try:
    with memoryview(data) as context:
        raise RuntimeError('preserved')
except RuntimeError as error:
    assert str(error) == 'preserved'
else:
    raise AssertionError('context exit swallowed exception')
check_released(context)
data.clear()

readonly = memoryview(b'abc')
for index in (3, -4, 1 << 40, -(1 << 40), 1 << 100, -(1 << 100)):
    try:
        readonly[index]
    except IndexError as error:
        expected = "cannot fit 'int' into an index-sized integer" if abs(index) > (1 << 63) else 'index out of bounds on dimension 1'
        assert str(error) == expected
    else:
        raise AssertionError('out-of-range index accepted')
readonly.release()
check_released(readonly)
try:
    readonly.release(1)
except TypeError:
    pass
else:
    raise AssertionError('release accepted an argument')
print('memoryview release: PASS')
