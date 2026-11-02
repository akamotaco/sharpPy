"""Python/_warnings.c of CPython v3.12.15: warn and warn_explicit.

CPython's warnings.py takes these from here. The filter callback order and the
registry keys are those of the C module, which do not match the Python fallback
in warnings.py.
"""
import sys
import operator

filters = [
    ('default', None, DeprecationWarning, '__main__', 0),
    ('ignore', None, DeprecationWarning, None, 0),
    ('ignore', None, PendingDeprecationWarning, None, 0),
    ('ignore', None, ImportWarning, None, 0),
    ('ignore', None, ResourceWarning, None, 0),
]
_defaultaction = 'default'
_onceregistry = {}
_filters_version = 0
_omitted = object()


def _filters_mutated():
    global _filters_version
    _filters_version += 1


# get_warnings_attr: the attribute of the warnings module, once it is there.
def _warnings_attr(name, default):
    module = sys.modules.get('warnings')
    if module is None:
        return default
    try:
        return getattr(module, name)
    except AttributeError:
        return default


def warn(message, category=None, stacklevel=1, source=None, *, skip_file_prefixes=()):
    stacklevel = operator.index(stacklevel)
    if stacklevel > sys.maxsize or stacklevel < -sys.maxsize - 1:
        raise OverflowError('Python int too large to convert to C ssize_t')
    if isinstance(message, Warning):
        category = type(message)
    if category is None:
        category = UserWarning
    if not isinstance(category, type) or not issubclass(category, Warning):
        raise TypeError("category must be a Warning subclass, not '%s'" % type(category).__name__)
    if not isinstance(skip_file_prefixes, tuple):
        raise TypeError('skip_file_prefixes must be a tuple of strs.')
    for prefix in skip_file_prefixes:
        if not isinstance(prefix, str):
            raise TypeError("Found non-str '%s' in skip_file_prefixes." % type(prefix).__name__)
    if skip_file_prefixes:
        stacklevel = max(2, stacklevel)
    try:
        frame = sys._getframe(1)
        for index in range(max(1, stacklevel) - 1):
            if frame is None:
                break
            frame = frame.f_back
            while frame is not None and (('importlib' in frame.f_code.co_filename and '_bootstrap' in frame.f_code.co_filename)
                                         or any(frame.f_code.co_filename.startswith(prefix) for prefix in skip_file_prefixes)):
                frame = frame.f_back
        if frame is None:
            raise ValueError()
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
    if module is None:
        return
    if registry is not None and not isinstance(registry, dict):
        raise TypeError("'registry' must be a dict or None")
    if module is _omitted:
        module = filename or '<unknown>'
        if module[-3:] == '.py':
            module = module[:-3]
    supplied_registry = registry is not None
    if registry is None:
        registry = {}
    if isinstance(message, Warning):
        text, category = str(message), type(message)
    else:
        text = message
        message = category(message)
    key = (text, category, lineno)
    if registry.get('version') != _filters_version:
        registry.clear()
        registry['version'] = _filters_version
    if registry.get(key):
        return
    item = None
    active_filters = _warnings_attr('filters', filters)
    if not isinstance(active_filters, list):
        raise ValueError('_warnings.filters must be a list')
    index = 0
    while index < list.__len__(active_filters):
        item = list.__getitem__(active_filters, index)
        if not isinstance(item, tuple) or tuple.__len__(item) != 5:
            raise ValueError("_warnings.filters item %s isn't a 5-tuple" % index)
        index += 1
        if type(item) is tuple:
            action, msg, cat, mod, line = item
        else:
            action, msg, cat, mod, line = (tuple.__getitem__(item, position) for position in range(5))
        if not isinstance(action, str):
            raise TypeError("action must be a string, not '%s'" % type(action).__name__)
        # _warnings.c evaluates both matchers before the subclass check.
        good_message = msg is None or (msg == text if type(msg) is str else bool(msg.match(text)))
        good_module = mod is None or (mod == module if type(mod) is str else bool(mod.match(module)))
        good_category = issubclass(category, cat)
        # PyLong_AsSsize_t requires an actual int and bypasses subclass hooks.
        # This conversion occurs even when either matcher returned false.
        if not isinstance(line, int):
            raise TypeError('an integer is required')
        line = operator.index(line)
        if line > sys.maxsize or line < -sys.maxsize - 1:
            raise OverflowError('Python int too large to convert to C ssize_t')
        if good_message and good_category and good_module and (line == 0 or lineno == line):
            break
    else:
        item = None
        action = _warnings_attr('defaultaction', _defaultaction)
        if not isinstance(action, str):
            raise TypeError("_warnings.defaultaction must be a string, not '%s'" % type(action).__name__)
    original_action = action
    if type(action) is not str:
        # Native action dispatch compares Unicode contents, not Python __eq__.
        action = ''.join((action,))
    if action == 'ignore':
        return
    if action == 'error':
        raise message
    if action == 'once':
        registry[key] = 1
        oncekey = (text, category)
        target = registry if supplied_registry else _warnings_attr('onceregistry', _onceregistry)
        if target.get('version') != _filters_version:
            target.clear()
            target['version'] = _filters_version
        if target.get(oncekey):
            return
        target[oncekey] = 1
    elif action == 'module':
        registry[key] = 1
        altkey = (text, category)
        if registry.get(altkey):
            return
        registry[altkey] = 1
    elif action == 'default':
        registry[key] = 1
    elif action != 'always':
        if supplied_registry:
            registry[key] = 1
        raise RuntimeError('Unrecognized action (%r) in warnings.filters:\n %r' % (original_action, item))
    import warnings
    warnings._showwarnmsg(warnings.WarningMessage(message, category, filename, lineno, source=source))
