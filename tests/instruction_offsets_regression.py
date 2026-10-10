# Force wide jump operands, constant indices and exception-handler offsets.
source = 'def wide(flag):\n    total = 0\n    try:\n        for i in range(3):\n            if flag:\n'
source += ''.join('                total += ' + str(i) + '\n' for i in range(1, 301))
source += '''            else:
                total += 7
        raise ValueError('done')
    except ValueError as error:
        assert str(error) == 'done'
        total += 11
    finally:
        total += 13
    return total
assert wide(True) == 135474
assert wide(False) == 45
print('wide jumps and exception offsets: PASS')
'''
exec(source)
