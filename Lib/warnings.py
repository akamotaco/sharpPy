"""
Simple warnings module for SharpPy
Compatible with CPython 3.12 warnings API
"""

# Default CPython filters. The native throw warning path shares this list.
_filters = [
    ('default', None, DeprecationWarning, '__main__', 0),
    ('ignore', None, DeprecationWarning, None, 0),
    ('ignore', None, PendingDeprecationWarning, None, 0),
    ('ignore', None, ImportWarning, None, 0),
    ('ignore', None, ResourceWarning, None, 0),
]
filters = _filters
_default_action = "default"
_filters_version = 0

class WarningMessage:
    """Holds information about a warning message."""
    def __init__(self, message, category, filename, lineno, file=None, line=None):
        self.message = message
        self.category = category
        self.filename = filename
        self.lineno = lineno
        self.file = file
        self.line = line

def warn(message, category=None, stacklevel=1):
    """Issue a warning, or maybe ignore it or raise an exception."""
    if category is None:
        category = UserWarning

    # Simple implementation: just print
    if isinstance(message, Warning):
        text = str(message)
    else:
        text = str(message)

    # Format: category: message
    print(f"{category.__name__}: {text}")

def warn_explicit(message, category, filename, lineno, module=None, registry=None, module_globals=None):
    """Low-level warning interface."""
    warn(message, category, stacklevel=1)

def simplefilter(action, category=Warning, lineno=0, append=False):
    """Add a simple entry to the warnings filter list."""
    global _filters_version
    assert action in ('error', 'ignore', 'always', 'default', 'module', 'once'), 'invalid action: %r' % action
    assert isinstance(lineno, int) and lineno >= 0, 'lineno must be an int >= 0'
    _filters_version += 1
    item = (action, None, category, None, lineno)
    if append:
        if item not in _filters:
            _filters.append(item)
    else:
        if item in _filters:
            _filters.remove(item)
        _filters.insert(0, item)

def filterwarnings(action, message="", category=Warning, module="", lineno=0, append=False):
    """Insert an entry into the warnings filter list."""
    # Simplified implementation
    simplefilter(action, category, lineno, append)

def resetwarnings():
    """Clear the warnings filter list."""
    global _filters_version
    _filters_version += 1
    _filters.clear()

# Warning action types
_ACTIONS = {
    "default",
    "error",
    "ignore",
    "always",
    "module",
    "once"
}

# Note: Default filters installation removed for simplicity

__all__ = [
    'warn', 'warn_explicit', 'simplefilter', 'filterwarnings',
    'resetwarnings', 'WarningMessage'
]
