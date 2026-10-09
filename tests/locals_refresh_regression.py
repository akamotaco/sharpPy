"""Repeated synchronization must preserve current values, deletion and None."""
import sys

def refresh(seed):
    observed = locals()
    for step in range(20):
        value = seed + step
        assert locals() is observed
        assert observed['value'] == seed + step
        del value
        assert 'value' not in locals()
        value = None
        assert locals()['value'] is None
        assert sys._getframe().f_locals is observed
    return observed
first = refresh(1)
second = refresh(100)
assert first is not second
assert first['seed'] == 1 and second['seed'] == 100
print('locals refresh regression: PASS')
