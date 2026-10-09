"""Known gap: retained module views must observe namespace writes."""
namespace = globals()
keys = namespace.keys()
items = namespace.items()
module_view_value = 7
assert 'module_view_value' in keys
assert 'module_view_value' in list(keys)
assert ('module_view_value', 7) in items
module_view_value = 8
assert ('module_view_value', 8) in items
del module_view_value
assert 'module_view_value' not in keys
print('module mapping retained views: PASS')
