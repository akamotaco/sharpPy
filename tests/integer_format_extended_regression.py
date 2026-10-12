"""Integer width/grouping/base formatting must not narrow through Int64."""
values = [0, 1, -1, 1234567, -1234567, -(2**63), 2**80 + 7, -(2**160 + 19)]
specs = ['d', '25d', '+025d', ' 025d', '#x', '#030x', '#_x', '#_b', '#_o',
         ',d', '_d', '020,d', '020_d', '*^30d', '0<25d', '=+25d', 'n', 'X']
for value in values:
    for spec in specs:
        print(value, spec, format(value, spec))
for value in (65, 0x1f600):
    for spec in ('c', '5c', '^5c', '05c'):
        print('char', spec, '[' + format(value, spec) + ']')
for spec in ('.2d', '.d', '+c', '-c', '#c', ',x', '_n', ',_d', '_,d', 'z', 'dd', 'q'):
    try: format(123, spec)
    except ValueError as error: print('error', spec, str(error))
    else: raise AssertionError(spec)
import sys
previous = sys.get_int_max_str_digits()
large = 10 ** 640
try:
    sys.set_int_max_str_digits(640)
    for spec in ('650d', ',d', '_d', '+d', 'n'):
        try: format(large, spec)
        except ValueError as error: print('limit', spec, str(error))
        else: raise AssertionError(spec)
    print('unlimited base', len(format(large, 'b')), len(format(large, 'x')))
finally:
    sys.set_int_max_str_digits(previous)
print('integer formats: PASS')
