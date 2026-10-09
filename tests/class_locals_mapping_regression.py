"""Class locals must expose the live mapping used for class body execution."""
import sys

outside = 100

class Example:
    namespace = locals()
    frame = sys._getframe()
    assert namespace is locals()
    assert namespace is frame.f_locals
    created = 7
    assert namespace['created'] == 7
    namespace['injected'] = 8
    assert injected == 8
    del created
    assert 'created' not in namespace

try:
    class MissingDelete:
        del outside
except NameError:
    pass
else:
    raise AssertionError('class deletion removed a global name')

assert outside == 100
assert Example.injected == 8
assert not hasattr(Example, 'created')
assert Example.frame.f_locals is Example.namespace
assert Example.frame.f_locals['injected'] == 8
print('class locals mapping regression: PASS')
