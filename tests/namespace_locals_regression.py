"""Basic non-function namespaces and Python 3.12 inline comprehensions."""
module_value = 3
assert locals()['module_value'] == 3

class Example:
    class_value = 4
    assert locals()['class_value'] == 4
assert Example.class_value == 4

def comprehension(value):
    snapshots = [locals().copy() for index in range(1)]
    assert snapshots[0]['value'] == 8
    assert snapshots[0]['index'] == 0
    assert 'index' not in locals()
comprehension(8)
print('namespace locals regression: PASS')
