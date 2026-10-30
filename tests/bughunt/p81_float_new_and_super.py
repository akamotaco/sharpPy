# P0-43b: each builtin base builds its own subtype instance in its own tp_new.
# Everything here was measured against CPython 3.12.15 first; the line that used to fail is
# D(2.0), where super().__new__(cls, v) fell through to object.__new__ because float had no
# __new__ slot (Objects/floatobject.c:1644 float_new, :1677 float_subtype_new).


class C(float):
    pass


class DF(float):
    def __new__(cls, v=0.0):
        return super().__new__(cls, v * 2)


c = C(1.5)
print(type(c).__name__, c, c + 1, repr(c), float(c), c.real)
c.attr = 7
print(c.attr, C(), C('2.5'), C(3), C(b'1.25'))
print(DF(2.0), type(DF(2.0)).__name__)
for label, fn in (('kwargs', lambda: C(x=1.5)), ('two args', lambda: C(1, 2)),
                  ('bad text', lambda: C('z'))):
    try:
        print(label, '->', fn())
    except Exception as e:
        print(label, '->', type(e).__name__ + ':', e)
print(float(), float(1), float('1.5'), float('  1.5  '), float(True))
try:
    print(float(x=1.5))
except Exception as e:
    print('float(x=) ->', type(e).__name__ + ':', e)


# the same super().__new__ shape on the other payload bases
class DI(int):
    def __new__(cls, v=0):
        return super().__new__(cls, v + 1)


class DS(str):
    def __new__(cls, v=''):
        return super().__new__(cls, v + '!')


class DT(tuple):
    def __new__(cls, v=()):
        return super().__new__(cls, list(v) + [9])


class DB(bytes):
    def __new__(cls, v=b''):
        return super().__new__(cls, v + b'!')


print(DI(1), type(DI(1)).__name__, DI(1) + 1, hash(DI(1)) == 2)
print(DS('a'), type(DS('a')).__name__, DS('a').upper(), len(DS('a')))
print(DT([1]), type(DT([1])).__name__, DT([1])[1], len(DT([1])))
print(DB(b'a'), type(DB(b'')).__name__, len(DB(b'a')), DB(b'a')[1])
print(C.__mro__[1].__name__, DF.__mro__[1].__name__, DI.__mro__[1].__name__)
