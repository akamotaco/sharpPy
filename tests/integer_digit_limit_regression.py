"""Interpreter digit limit, validation and input/output decimal boundaries."""
import sys
original = sys.get_int_max_str_digits()
def show(label, function):
    try: print(label, function())
    except Exception as error: print(label, type(error).__name__, str(error))
try:
    print('default', original)
    for limit in (-1, 1, 639, 640, 0, 4300, 2**31, -2**31-1, 1.5, '640', None, True, False):
        show('set', lambda: sys.set_int_max_str_digits(limit))
        print('state', sys.get_int_max_str_digits())
    class Index:
        def __index__(self): return 640
    show('index', lambda: sys.set_int_max_str_digits(Index()))
    for n in (639, 640, 641):
        text = '1' * n
        show('parse', lambda: int(text) > 0)
        show('zeros', lambda: int('0' * n))
        show('repr', lambda: len(repr(10 ** (n-1))))
        show('str-negative', lambda: len(str(-(10 ** (n-1)))))
        show('format-decimal', lambda: len(format(10 ** (n-1), 'd')))
        show('format-default', lambda: len(format(10 ** (n-1), '')))
    show('binary', lambda: int('1' * 1000, 2) > 0)
    sys.set_int_max_str_digits(0)
    print('unlimited', len(str(int('1' * 5000))))
    show('get-args', lambda: sys.get_int_max_str_digits(1))
    show('get-kw', lambda: sys.get_int_max_str_digits(x=1))
    show('set-args', lambda: sys.set_int_max_str_digits())
    show('set-kw', lambda: sys.set_int_max_str_digits(maxdigits=640))
    show('set-too-many', lambda: sys.set_int_max_str_digits(640, 640))
    show('set-duplicate', lambda: sys.set_int_max_str_digits(640, maxdigits=640))
    show('set-invalid-kw', lambda: sys.set_int_max_str_digits(wrong=640))
finally:
    sys.set_int_max_str_digits(original)
print('restored', sys.get_int_max_str_digits())
