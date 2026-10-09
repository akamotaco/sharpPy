"""Failed keyword/class calls must not affect later calls or module locals."""
namespace = locals()

class FailingMeta(type):
    def __new__(mcls, name, bases, mapping):
        raise ValueError('creation failed')

try:
    class Broken(metaclass=FailingMeta):
        value = 1
except ValueError:
    pass
else:
    raise AssertionError('metaclass exception was swallowed')
assert len([1, 2]) == 2
assert locals() is namespace

try:
    class BrokenBody:
        value = 2
        raise RuntimeError('body failed')
except RuntimeError:
    pass
else:
    raise AssertionError('class body exception was swallowed')
assert locals() is namespace
assert sum([1, 2]) == 3
assert 'value' not in locals()
print('call failure state regression: PASS')
