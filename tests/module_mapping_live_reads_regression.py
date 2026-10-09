"""String-key reads must observe current module bindings without refreshing a copy."""
existing_binding = 1
namespace = globals()
existing_binding = 2
assert namespace['existing_binding'] == 2
assert namespace.get('existing_binding') == 2
assert dict.get(namespace, 'existing_binding') == 2
assert 'existing_binding' in namespace
new_binding = 3
assert namespace['new_binding'] == 3
assert namespace.get('new_binding') == 3
assert 'new_binding' in namespace
del existing_binding
assert 'existing_binding' not in namespace
assert namespace.get('existing_binding', 'missing') == 'missing'
try:
    namespace['existing_binding']
except KeyError:
    pass
else:
    raise AssertionError('deleted binding must not return a stale value')
assert namespace.get('never_created') is None
assert namespace.get('never_created', 4) == 4
assert 'never_created' not in namespace
namespace['written'] = 5
assert written == 5
assert namespace['written'] == 5
namespace[1000] = 6
assert namespace[int('1000')] == 6
assert namespace.get(int('1000')) == 6
assert int('1000') in namespace
assert namespace.pop(int('1000')) == 6
assert namespace is globals() and namespace is locals()
print('module mapping live reads: PASS')
