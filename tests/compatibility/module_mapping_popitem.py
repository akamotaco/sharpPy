"""Known gap: popitem through globals must remove the actual binding."""
namespace = globals()
namespace['popitem_binding'] = 17
assert namespace.popitem() == ('popitem_binding', 17)
try:
    popitem_binding
except NameError:
    pass
else:
    raise AssertionError('popitem must remove the module binding')
print('module mapping popitem: PASS')
