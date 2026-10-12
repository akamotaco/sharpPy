"""Requires native str-subclass payload; do not substitute constructor args."""
import warnings

class Action(str):
    def __eq__(self, other):
        raise AssertionError('action equality callback')

with warnings.catch_warnings(record=True) as log:
    warnings.filters = [(Action('always'), None, Warning, None, 0)]
    warnings.warn_explicit('test', UserWarning, 'test.py', 1)
    print('str subclass action', len(log))
