"""Formatting decisions after rounding, grouping, and common entry points."""
values = (0.0, -0.0, 1.25, 12.5, 9.99, 999.9, 1e-5, 1e-4,
          1e15, 1e16, 1.2345678901234567, -12345.5, float('inf'), float('nan'))
specs = ('', '12', '.0', '.1', '.2', '.3', 'g', '.0g', '.2g', '#g',
         '#.0g', '#.2g', '#.0e', '#.0%', '012,.2f', '014_.1f',
         '>012.2f', '*=012.2f', '🍀^14.2f', 'n', ',g', '_e', 'z.0f')
for index, value in enumerate(values):
    for spec in specs:
        try:
            print(index, spec, repr(format(value, spec)))
        except Exception as error:
            print(index, spec, type(error).__name__, str(error))
for spec in (',n', '_n', ',_f', '_,f', '.2147483648f', '999999999999999999999f',
             '🍀', ',🍀', chr(0), '٨.٢f', '𝟚.2f'):
    try:
        print('invalid', spec, format(1.0, spec))
    except Exception as error:
        print('invalid', spec, type(error).__name__, str(error))
for value in (1.0, 12.5, 1e16, -0.0):
    print('entry', '{}'.format(value), '{:#.2g}'.format(value), f'{value:#.2g}')

events = []
class Custom:
    def __getattribute__(self, name):
        events.append(('attribute', name))
        return object.__getattribute__(self, name)
    def __format__(self, spec):
        events.append(('format', spec))
        return 'custom:' + spec
custom = Custom()
print(format(custom), '{}'.format(custom), f'{custom}', events)
class Failure:
    def __format__(self, spec):
        raise AttributeError('format callback failed')
class BadResult:
    def __format__(self, spec):
        return 42
for obj in (Failure(), BadResult()):
    for operation in (lambda: format(obj), lambda: '{}'.format(obj), lambda: f'{obj}'):
        try:
            print(operation())
        except Exception as error:
            print(type(error).__name__, str(error))
