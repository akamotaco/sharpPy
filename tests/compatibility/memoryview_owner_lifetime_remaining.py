"""Last-reference release, including owners other than a Python name.

Each case reports independently so one failure cannot hide the other boundaries.
"""
def resize(label, data):
    try:
        data.append(100)
    except BufferError:
        print(label, 'blocked')
    else:
        print(label, 'released')

data = bytearray(b'abc')
view = memoryview(data)
alias = view
del view
resize('live alias', data)
del alias
resize('last alias', data)

data = bytearray(b'abc')
owner = [memoryview(data)]
resize('list owner', data)
owner.clear()
resize('cleared list', data)

data = bytearray(b'abc')
owner = {'view': memoryview(data)}
resize('dict owner', data)
del owner['view']
resize('deleted dict item', data)

data = bytearray(b'abc')
owner = (memoryview(data),)
del owner
resize('deleted tuple', data)

data = bytearray(b'abc')
def scoped():
    view = memoryview(data)
    resize('live frame', data)
scoped()
resize('returned frame', data)

data = bytearray(b'abc')
def capture():
    view = memoryview(data)
    def read(): return view[0]
    return read
reader = capture()
resize('live closure', data)
del reader
resize('deleted closure', data)

data = bytearray(b'abc')
view = memoryview(data)
method = view.release
del view
resize('bound method owner', data)
del method
resize('deleted method', data)

data = bytearray(b'abc')
view = memoryview(data)
child = view[1:]
del view
resize('live child', data)
del child
resize('last child', data)

data = bytearray(b'abc')
memoryview(data)
resize('discarded expression', data)
