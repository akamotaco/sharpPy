"""Known gap: class locals and f_locals must expose the class mapping."""
import sys

class Example:
    namespace = locals()
    assert namespace is locals()
    assert namespace is sys._getframe().f_locals
    created_after_observation = 7
    assert namespace['created_after_observation'] == 7
    namespace['injected'] = 8
    assert injected == 8
assert Example.injected == 8
print('class locals mapping: PASS')
