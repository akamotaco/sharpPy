"""Known gap: source namespace insertion order must survive deletion/reinsertion."""
def check_order():
    namespace = globals()
    namespace['ordered_first'] = 1
    namespace['ordered_second'] = 2
    del namespace['ordered_first']
    namespace['ordered_first'] = 3
    copied = namespace.copy()
    keys = [key for key in copied if key in ('ordered_first', 'ordered_second')]
    assert keys == ['ordered_second', 'ordered_first']

check_order()
print('module mapping copy order: PASS')
