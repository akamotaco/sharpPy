"""Namespace mutations must update bindings without replaying stale entries."""
namespace = globals()
mutation_existing = 20
assert namespace.setdefault('mutation_existing', 99) == 20
assert mutation_existing == 20
assert namespace.setdefault('mutation_created', 21) == 21
assert mutation_created == 21
assert namespace.pop('mutation_created') == 21
try:
    mutation_created
except NameError:
    pass
else:
    raise AssertionError('pop must delete the binding')

mutation_later = 22
del namespace['mutation_later']
try:
    mutation_later
except NameError:
    pass
else:
    raise AssertionError('del must delete a binding created after observation')

namespace['mutation_updated'] = 1
mutation_updated = 2
assert namespace.pop('mutation_updated') == 2
assert namespace.pop('mutation_missing', 'default') == 'default'
try:
    del namespace['mutation_missing']
except KeyError:
    pass
else:
    raise AssertionError('missing del must raise KeyError')

namespace['mutation_unrelated'] = 1
mutation_unrelated = 2
namespace.update({'mutation_target': 3})
assert mutation_unrelated == 2
assert mutation_target == 3
namespace.update(namespace)
assert mutation_unrelated == 2
dict.update(namespace, {'mutation_descriptor': 5})
assert mutation_descriptor == 5
from types import MappingProxyType
namespace.update(MappingProxyType({'mutation_proxy': 6}))
assert mutation_proxy == 6
namespace |= {'mutation_merged': 4}
assert mutation_merged == 4
assert mutation_unrelated == 2

namespace[1000] = 'non-string'
assert namespace.pop(int('1000')) == 'non-string'
assert namespace.setdefault(2000, 'extra') == 'extra'
assert namespace.pop(2000) == 'extra'
print('module mapping mutations: PASS')
