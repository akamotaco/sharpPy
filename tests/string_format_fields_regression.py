def check(template, *args, **kwargs):
    try:
        print(repr(template), repr(template.format(*args, **kwargs)))
    except Exception as exc:
        print(repr(template), type(exc).__name__, str(exc))

check('{0:{1}.{2}f}', 12.345, 9, 2)
check('{:{}.{}}', 12.345, 9, 4)
check('{value:{width}.{precision}f}', value=12.345, width=9, precision=2)
check('{{{0}}} {{}}', 42)
check('{0[1]} {0[0]}', [10, 20])
check('{0[a:b]} {0[a!b]} {0[a}b]}', {'a:b': 1, 'a!b': 2, 'a}b': 3})
check('{0!a}', 'é🍀')
check('{0!s:>{1}}', 42, 5)
for template in ('{', '}', '{0', '{0!', '{0!ss}', '{0:', '{0:{1:{2}}}',
                 '{} {0}', '{0} {}', '{0[]}', '{0.}', '{0[x]q}', '{0[x}',
                 '{2}', '{0!z}', '{0!é}', '{0!🍀}', '{0!\x00}', '{0{a}}'):
    check(template, {'x': 7}, 3, 2)
check('{٠}', 17)
check('{𝟘}', 19)
check('{999999999999999999999999999}', 1)

events = []
class Traced:
    def __getattribute__(self, name):
        events.append(('attr', name))
        return self
    def __getitem__(self, key):
        events.append(('item', key))
        return self
    def __str__(self):
        events.append('str')
        return 'value'
    def __repr__(self):
        events.append('repr')
        return 'value'
    def __format__(self, spec):
        events.append(('format', spec))
        return '5'

obj = Traced()
print('unused', 'literal'.format(obj), events)
events.clear()
print('order', '{0.attr[key]!s:>{1}}'.format(obj, obj), events)
events.clear()
print('map', '{a[key].attr:{width}}'.format_map(obj), events)
for template in ('{}', '{0}', '{x:{}}'):
    try:
        print(template.format_map({'x': 1}))
    except Exception as exc:
        print('map error', type(exc).__name__, str(exc))
print('string field process: PASS')
