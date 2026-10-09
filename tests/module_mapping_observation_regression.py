"""Module length, truth and shallow copy must observe current bindings."""
def observe():
    global observed_binding
    namespace = globals()
    initial = len(namespace)
    observed_binding = 10
    assert len(namespace) == initial + 1
    snapshot = namespace.copy()
    assert snapshot['observed_binding'] == 10
    assert len(snapshot) == len(namespace)
    observed_binding = 11
    assert snapshot['observed_binding'] == 10
    assert namespace.copy()['observed_binding'] == 11
    assert dict.copy(namespace)['observed_binding'] == 11
    del observed_binding
    assert len(namespace) == initial
    namespace[1] = 'integer'
    namespace[True] = 'boolean'
    assert len(namespace) == initial + 1
    assert namespace.copy()[1] == 'boolean'
    assert namespace.pop(True) == 'boolean'
    assert len(namespace) == initial
    assert namespace.pop(9999, 'missing') == 'missing'
    assert len(namespace) == initial
    try:
        del namespace[9999]
    except KeyError:
        pass
    else:
        raise AssertionError('missing deletion must fail')
    assert len(namespace) == initial
    assert bool(namespace)
    assert 'observed_binding' not in namespace.copy()
    namespace[1000] = 'extra'
    assert len(namespace) == initial + 1
    namespace[int('1000')] = 'changed'
    assert len(namespace) == initial + 1
    assert namespace.copy()[int('1000')] == 'changed'
    assert namespace.pop(int('1000')) == 'changed'
    assert len(namespace) == initial
    namespace.setdefault(2000, 'default')
    assert len(namespace) == initial + 1
    del namespace[int('2000')]
    assert len(namespace) == initial
    namespace.update({3000: 3})
    assert len(namespace) == initial + 1
    assert namespace.popitem() == (3000, 3)
    assert len(namespace) == initial

observe()
print('module mapping observation: PASS')
