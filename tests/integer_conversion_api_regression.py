def show(label, action):
    try: print(label, action())
    except Exception as error: print(label, type(error).__name__, str(error))

for value in (b'123', b'-0xff', b'1_234', b'', b'bad', b'\xa0123', b'1\x002'):
    for factory in (bytes, bytearray):
        for base in (0, 10, 16):
            show((factory.__name__, value, base), lambda: int(factory(value), base=base))
for label, action in (
    ('missing', lambda: int(base=16)),
    ('posonly', lambda: int(x='1')),
    ('unknown', lambda: int('1', foo=2)),
    ('duplicate', lambda: int('1', 10, base=16)),
    ('huge-base', lambda: int('1', 2**100)),
    ('float-base', lambda: int('1', 2.0)),
    ('number-base', lambda: int(1, 10)),
    ('none-base', lambda: int(None, 10)),
    ('new-bytes', lambda: int.__new__(int, b'ff', base=16)),
    ('empty-bytes', lambda: int(bytes())),
    ('encoded-bytes', lambda: int(bytes('123', 'ascii'))),
): show(label, action)
events = []
class Base:
    def __index__(self):
        events.append('index')
        return 16
show('index', lambda: int(b'ff', Base()))
show('index-number', lambda: int(1, Base()))
print(events)
