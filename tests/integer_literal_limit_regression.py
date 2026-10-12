"""The active integer conversion limit also applies while parsing source."""
import sys
previous = sys.get_int_max_str_digits()
try:
    sys.set_int_max_str_digits(640)
    for mode in ('eval', 'exec', 'single'):
        for count in (639, 640, 641):
            source = '1' * count
            try:
                compile(source, 'literal_probe.py', mode)
            except SyntaxError as error:
                print(mode, count, error.msg, error.filename, error.lineno, error.offset, error.end_offset)
            else:
                print(mode, count, 'accepted')
    compile('0x' + 'f' * 5000, 'literal_probe.py', 'eval')
    compile('1.' + '0' * 700, 'literal_probe.py', 'eval')
    for source in ('1_' * 640 + '1',):
        try: compile(source, 'digits.py', 'eval')
        except SyntaxError as error: print('counted digits', error.msg, error.offset, error.end_offset)
        else: raise AssertionError('oversized literal accepted')
    compile('0' * 5000, 'zero.py', 'eval')
    print('zero literal accepted')
    sys.set_int_max_str_digits(0)
    compile('1' * 5000, 'literal_probe.py', 'eval')
    print('hex float and disabled limit: PASS')
finally:
    sys.set_int_max_str_digits(previous)
