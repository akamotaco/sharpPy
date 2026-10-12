import os
import sys

path = ('user://' if sys._getframe().f_code.co_filename.startswith('res://') else '') + '__sharppy_file_io_round129.tmp'
try:
    file = open(path, 'wb')
    print('eager create', os.path.exists(path), file.closed)
    print('binary write', file.write(b'abcdefgh'), file.write(bytearray(b'ij')))
    print('enter identity', file.__enter__() is file)
    file.close()
    file.close()
    print('closed', file.closed)
    with open(path, 'rb') as file:
        print('binary reads', file.read(0), file.read(3), file.read(2), file.read(), file.read())
    with open(path, 'wb') as file:
        file.write(b'xy')
    with open(path, 'rb') as file:
        print('truncated', file.read())
    with open(path, 'ab') as file:
        file.write(b'z')
    with open(path, 'r+b') as file:
        print('update', file.read(1), file.write(b'!'), file.seek(0), file.read())
    with open(path, 'wb') as file:
        view = memoryview(b'abc')
        print('view write', file.write(view))
        view.release()
    with open(path, 'w', encoding='utf-8', newline='') as file:
        print('text write length', file.write('a🍀é\r\nb\rc\nlast'))
    with open(path, 'r', encoding='utf-8') as file:
        print('unicode read', repr(file.read(2)), repr(file.read(1)), repr(file.read()))
    with open(path, 'r', encoding='utf-8') as file:
        print('lines', file.readlines())
    with open(path, 'r', encoding='utf-8', newline='') as file:
        print('raw lines', file.readlines())
    print('exit result', file.__exit__(None, None, None))
    with open(path, 'w', encoding='utf-8') as file:
        file.write('end')
    with open(path, 'rb') as file:
        print('text truncate', file.read())
    for mode in ('rr', 'R', 'rbt', '', 'rw'):
        try:
            open(path, mode)
        except Exception as exc:
            print('mode', mode, type(exc).__name__, str(exc))
    try:
        open(path, 'xb')
    except Exception as exc:
        print('exclusive', type(exc).__name__, exc.errno, exc.filename == path)
    for operation in (lambda: file.read(1), lambda: file.__enter__()):
        try:
            operation()
        except Exception as exc:
            print('closed operation', type(exc).__name__, str(exc))
finally:
    if os.path.exists(path): os.remove(path)
try:
    open(path, 'rb')
except Exception as exc:
    print('eager missing', type(exc).__name__, exc.errno, exc.filename == path)
print('file IO: PASS')
