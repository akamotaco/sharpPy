"""Special values, negative zero, rounding to zero, and format validation."""
values = (float('inf'), float('-inf'), float('nan'), -0.0, 0.0, -0.001, 12.5)
specs = ('f', 'F', '+08F', '=+8g', '*>10G', '%', '.0f', '+.2e', 'z.2f', '+z.2f')
for index, value in enumerate(values):
    for spec in specs:
        try: print(index, spec, repr(format(value, spec)))
        except Exception as error: print(index, spec, type(error).__name__, str(error))
for value in (float('inf'), 1.0):
    for spec in ('x', 'fgarbage', '.f'):
        try: print('invalid', format(value, spec))
        except Exception as error: print('invalid', type(error).__name__, str(error))
