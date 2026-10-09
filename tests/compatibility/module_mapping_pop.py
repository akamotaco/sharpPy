"""Known gap: pop through globals must remove the module binding."""
namespace = globals()
namespace['module_pop_value'] = 17
assert namespace is globals()
assert namespace is locals()
assert namespace.pop('module_pop_value') == 17
try:
    module_pop_value
except NameError:
    pass
else:
    raise AssertionError('pop must remove the global binding')
print('module mapping pop: PASS')
