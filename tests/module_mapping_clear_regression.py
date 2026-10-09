"""Clearing globals from a function removes module bindings."""
clear_me = 1

def clear_scope():
    namespace = globals()
    namespace['another'] = 2
    namespace[1000] = 'extra'
    namespace.clear()
    assert len(namespace) == 0
    assert not namespace
    assert len(namespace.copy()) == 0
    assert 'clear_me' not in globals()
    assert 'another' not in globals()
    assert 'clear_scope' not in globals()
    return True

assert clear_scope()
print('module mapping clear: PASS')
