"""importlib on SharpPy's import system: Lib/importlib/_bootstrap.py runs as _frozen_importlib, and
the path searches and their loaders are C# (_frozen_importlib_external), with the module-level
functions of CPython's _bootstrap_external (_external_functions.py) in it."""
__all__ = ['__import__', 'import_module', 'invalidate_caches', 'reload']

# Bootstrap help #####################################################

import _imp  # Just the builtin component, NOT the full Python module
import sys

try:
    import _frozen_importlib as _bootstrap
except ImportError:
    from . import _bootstrap
    _bootstrap._setup(sys, _imp)
else:
    # importlib._bootstrap is the built-in import, ensure we don't create
    # a second copy of the module.
    _bootstrap.__name__ = 'importlib._bootstrap'
    _bootstrap.__package__ = 'importlib'
    try:
        _bootstrap.__file__ = __file__.replace('__init__.py', '_bootstrap.py')
    except NameError:
        # __file__ is not guaranteed to be defined, e.g. if this code gets
        # frozen by a tool like cx_Freeze.
        pass
    sys.modules['importlib._bootstrap'] = _bootstrap

try:
    import _frozen_importlib_external as _bootstrap_external
except ImportError:
    # SharpPy has no Lib/importlib/_bootstrap_external.py of its own: its functions are these.
    from . import _external_functions as _bootstrap_external
    _bootstrap_external._bootstrap = _bootstrap
    _bootstrap._bootstrap_external = _bootstrap_external
else:
    _bootstrap_external.__name__ = 'importlib._bootstrap_external'
    _bootstrap_external.__package__ = 'importlib'
    sys.modules['importlib._bootstrap_external'] = _bootstrap_external

from _importlib_native import __import__, import_module, reload, invalidate_caches
