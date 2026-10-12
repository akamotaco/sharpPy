"""Warning filters and reporting for the SharpPy source distribution.

Uses CPython 3.12 warnings.py and _warnings.c: the accelerated implementation
defines filter callback order and registry keys. Native warnings share this path.
"""
import sys

filters = [
    ('default', None, DeprecationWarning, '__main__', 0),
    ('ignore', None, DeprecationWarning, None, 0),
    ('ignore', None, PendingDeprecationWarning, None, 0),
    ('ignore', None, ImportWarning, None, 0),
    ('ignore', None, ResourceWarning, None, 0),
]
defaultaction = 'default'
onceregistry = {}
_filters_version = 0
_omitted = object()

def _filters_mutated():
    global _filters_version
    _filters_version += 1

def _add_filter(item, append):
    if append:
        if item not in filters: filters.append(item)
    else:
        if item in filters: filters.remove(item)
        filters.insert(0, item)
    _filters_mutated()

def simplefilter(action, category=Warning, lineno=0, append=False):
    assert action in ('error', 'ignore', 'always', 'default', 'module', 'once'), 'invalid action: %r' % action
    assert isinstance(lineno, int) and lineno >= 0, 'lineno must be an int >= 0'
    _add_filter((action, None, category, None, lineno), append)

def filterwarnings(action, message='', category=Warning, module='', lineno=0, append=False):
    assert action in ('error', 'ignore', 'always', 'default', 'module', 'once'), 'invalid action: %r' % action
    assert isinstance(message, str), 'message must be a string'
    assert isinstance(category, type), 'category must be a class'
    assert issubclass(category, Warning), 'category must be a Warning subclass'
    assert isinstance(module, str), 'module must be a string'
    assert isinstance(lineno, int) and lineno >= 0, 'lineno must be an int >= 0'
    if message or module:
        import re
        message = re.compile(message, re.I) if message else None
        module = re.compile(module) if module else None
    else:
        message = module = None
    _add_filter((action, message, category, module, lineno), append)

def resetwarnings():
    filters[:] = []
    _filters_mutated()

class WarningMessage:
    def __init__(self, message, category, filename, lineno, file=None, line=None, source=None):
        self.message = message
        self.category = category
        self.filename = filename
        self.lineno = lineno
        self.file = file
        self.line = line
        self.source = source
        self._category_name = category.__name__ if category else None

def formatwarning(message, category, filename, lineno, line=None):
    result = '%s:%s: %s: %s\n' % (filename, lineno, category.__name__, message)
    if line is None:
        try:
            import linecache
            line = linecache.getline(filename, lineno)
        except Exception:
            line = None
    if line:
        result += '  ' + line.strip() + '\n'
    return result

def showwarning(message, category, filename, lineno, file=None, line=None):
    _showwarnmsg_impl(WarningMessage(message, category, filename, lineno, file, line))

_showwarning_orig = showwarning

def _showwarnmsg_impl(message):
    file = message.file if message.file is not None else sys.stderr
    if file is None: return
    text = formatwarning(message.message, message.category, message.filename, message.lineno, message.line)
    try: file.write(text)
    except OSError: pass

def _showwarnmsg(message):
    if showwarning is not _showwarning_orig:
        if not callable(showwarning): raise TypeError('warnings.showwarning() must be set to a function or method')
        showwarning(message.message, message.category, message.filename, message.lineno, message.file, message.line)
    else:
        _showwarnmsg_impl(message)

def warn(message, category=None, stacklevel=1, source=None, *, skip_file_prefixes=()):
    if isinstance(message, Warning): category = type(message)
    if category is None: category = UserWarning
    if not isinstance(category, type) or not issubclass(category, Warning):
        raise TypeError("category must be a Warning subclass, not '%s'" % type(category).__name__)
    if not isinstance(skip_file_prefixes, tuple): raise TypeError('skip_file_prefixes must be a tuple of strs.')
    if skip_file_prefixes: stacklevel = max(2, stacklevel)
    try:
        frame = sys._getframe(1)
        for index in range(max(1, stacklevel) - 1):
            frame = frame.f_back
            while frame is not None and (('importlib' in frame.f_code.co_filename and '_bootstrap' in frame.f_code.co_filename) or any(frame.f_code.co_filename.startswith(prefix) for prefix in skip_file_prefixes)):
                frame = frame.f_back
        if frame is None: raise ValueError()
        namespace = frame.f_globals
        filename = frame.f_code.co_filename
        lineno = frame.f_lineno
    except ValueError:
        namespace = sys.__dict__
        filename, lineno = 'sys', 1
    module = namespace.get('__name__', '<string>')
    registry = namespace.setdefault('__warningregistry__', {})
    warn_explicit(message, category, filename, lineno, module, registry, namespace, source)

def warn_explicit(message, category, filename, lineno, module=_omitted, registry=None, module_globals=None, source=None):
    lineno = int(lineno)
    if module is None: return
    if registry is not None and not isinstance(registry, dict): raise TypeError("'registry' must be a dict or None")
    if module is _omitted:
        module = filename or '<unknown>'
        if module[-3:] == '.py': module = module[:-3]
    supplied_registry = registry is not None
    if registry is None: registry = {}
    if isinstance(message, Warning):
        text, category = str(message), type(message)
    else:
        text = message
        message = category(message)
    key = (text, category, lineno)
    if registry.get('version') != _filters_version:
        registry.clear()
        registry['version'] = _filters_version
    if registry.get(key): return
    item = None
    for item in filters:
        action, msg, cat, mod, line = item
        # _warnings.c evaluates both matchers before the subclass check.
        good_message = msg is None or (msg == text if isinstance(msg, str) else bool(msg.match(text)))
        good_module = mod is None or (mod == module if isinstance(mod, str) else bool(mod.match(module)))
        good_category = issubclass(category, cat)
        if good_message and good_category and good_module and (line == 0 or lineno == line):
            break
    else:
        action = defaultaction
    if action == 'ignore': return
    if action == 'error': raise message
    if action == 'once':
        registry[key] = 1
        oncekey = (text, category)
        target = registry if supplied_registry else onceregistry
        if target.get('version') != _filters_version:
            target.clear()
            target['version'] = _filters_version
        if target.get(oncekey): return
        target[oncekey] = 1
    elif action == 'module':
        registry[key] = 1
        altkey = (text, category)
        if registry.get(altkey): return
        registry[altkey] = 1
    elif action == 'default': registry[key] = 1
    elif action != 'always': raise RuntimeError('Unrecognized action (%r) in warnings.filters: %s' % (action, item))
    _showwarnmsg(WarningMessage(message, category, filename, lineno, source=source))

class catch_warnings:
    def __init__(self, *, record=False, module=None, action=None, category=Warning, lineno=0, append=False):
        self._record = record
        self._module = sys.modules['warnings'] if module is None else module
        self._entered = False
        self._filter = None if action is None else (action, category, lineno, append)
    def __enter__(self):
        if self._entered: raise RuntimeError('Cannot enter catch_warnings twice')
        self._entered = True
        self._filters = self._module.filters
        self._module.filters = self._filters[:]
        self._module._filters_mutated()
        self._showwarning = self._module.showwarning
        self._showwarnmsg_impl = self._module._showwarnmsg_impl
        if self._filter is not None: self._module.simplefilter(*self._filter)
        if self._record:
            log = []
            self._module._showwarnmsg_impl = log.append
            self._module.showwarning = self._module._showwarning_orig
            return log
        return None
    def __exit__(self, *exc_info):
        if not self._entered: raise RuntimeError('Cannot exit catch_warnings without entering first')
        self._module.filters = self._filters
        self._module._filters_mutated()
        self._module.showwarning = self._showwarning
        self._module._showwarnmsg_impl = self._showwarnmsg_impl

__all__ = ['warn', 'warn_explicit', 'showwarning', 'formatwarning', 'filterwarnings', 'simplefilter', 'resetwarnings', 'catch_warnings']
