class Number(int): pass
for text, base in (
    ('0', 10), ('-0', 10), (' +123 ', 10), ('1_000_000', 10),
    ('9' * 80, 10), ('-' + '9' * 80, 10), ('f' * 100, 16),
    ('0x_' + 'f' * 100, 0), ('0b' + '1' * 300, 0),
    ('z' * 100, 36), ('1' * 200, 3), ('00_00', 0),
    ('\u2003+١٢٣٤٥٦٧٨٩٠\u2003', 10), ('１２３', 10),
    ('𝟙𝟚𝟛', 10), ('٠xFF', 0), ('0o_777', 8),
):
    normal = int(text, base)
    child = Number(text, base)
    explicit = int.__new__(Number, text, base)
    print(text, base, normal, child, explicit, type(child).__name__, type(explicit).__name__)
for text in ('9' * 100, '-' + '9' * 100, '  1_234  ', '１２３'):
    print('default', int(text), Number(text), int.__new__(Number, text))
for text, base in (('', 10), ('  ', 10), ('+', 10), ('- 1', 10), ('_1', 10),
                   ('1_', 10), ('1__0', 10), ('0x_', 0), ('0x__1', 0),
                   ('01', 0), ('00_1', 0), ('0b2', 0), ('0x1', 10),
                   ('12\x00', 10), ("'bad'", 10), ('²', 10), ('1 2', 10),
                   ('1', 1), ('1', 37), ('1', -2)):
    for constructor in (int, Number):
        try: constructor(text, base)
        except (ValueError, TypeError) as error: print(constructor.__name__, type(error).__name__, str(error))
print('limit valid', len(str(Number('1' * 4300))))
for constructor in (int, Number):
    try: constructor('1' * 4301)
    except ValueError as error: print(constructor.__name__, str(error))
