"""Source warning line."""
import os
import linecache
import warnings

size = os.stat(__file__).st_size
print('stat size', size > 0, isinstance(size, int))
print('source', linecache.getline(__file__, 1).strip())
print('warning source', warnings.formatwarning('probe', UserWarning, __file__, 1).splitlines()[1].strip())
print('missing source', len(warnings.formatwarning('probe', UserWarning, '<missing>', 1).splitlines()))
