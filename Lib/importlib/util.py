"""What Lib/importlib/util.py offers on the native import system."""
import sys
from .machinery import ModuleSpec


def resolve_name(name, package):
    """Resolve a relative module name to an absolute one."""
    if not name.startswith('.'):
        return name
    elif not package:
        raise ImportError(f'no package specified for {repr(name)} '
                          '(required for relative module names)')
    level = 0
    for character in name:
        if character != '.':
            break
        level += 1
    bits = package.rsplit('.', level - 1)
    if len(bits) < level:
        raise ImportError('attempted relative import beyond top-level package')
    base = bits[0]
    return f'{base}.{name[level:]}' if name[level:] else base


def cache_from_source(path, debug_override=None, *, optimization=None):
    return path + 'c'


def source_from_cache(path):
    return path[:-1]


def find_spec(name, package=None):
    fullname = resolve_name(name, package) if name.startswith('.') else name
    if fullname in sys.modules:
        return getattr(sys.modules[fullname], '__spec__', None)
    return None
