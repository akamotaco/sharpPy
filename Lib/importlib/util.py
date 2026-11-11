"""Utility code for constructing importers, etc. (Lib/importlib/util.py over SharpPy's machinery)."""
from ._bootstrap import module_from_spec
from ._bootstrap import _resolve_name
from ._bootstrap import spec_from_loader
from ._bootstrap import _find_spec
from ._bootstrap_external import MAGIC_NUMBER
from ._bootstrap_external import _RAW_MAGIC_NUMBER
from ._bootstrap_external import cache_from_source
from ._bootstrap_external import decode_source
from ._bootstrap_external import source_from_cache
from ._bootstrap_external import spec_from_file_location

import _imp
import sys


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
    return _resolve_name(name[level:], package, level)


def _find_spec_from_path(name, path=None):
    """Return the spec for the specified module (Lib/importlib/util.py)."""
    if name not in sys.modules:
        return _find_spec(name, path)
    else:
        module = sys.modules[name]
        if module is None:
            return None
        try:
            spec = module.__spec__
        except AttributeError:
            raise ValueError(f'{name}.__spec__ is not set') from None
        else:
            if spec is None:
                raise ValueError(f'{name}.__spec__ is None')
            return spec


def find_spec(name, package=None):
    """Return the spec for the specified module (Lib/importlib/util.py)."""
    fullname = resolve_name(name, package) if name.startswith('.') else name
    if fullname not in sys.modules:
        parent_name = fullname.rpartition('.')[0]
        if parent_name:
            parent = __import__(parent_name, fromlist=['__path__'])
            try:
                parent_path = parent.__path__
            except AttributeError as e:
                raise ModuleNotFoundError(
                    f"__path__ attribute not found on {parent_name!r} "
                    f"while trying to find {fullname!r}", name=fullname) from e
        else:
            parent_path = None
        return _find_spec(fullname, parent_path)
    else:
        module = sys.modules[fullname]
        if module is None:
            return None
        try:
            spec = module.__spec__
        except AttributeError:
            raise ValueError(f'{name}.__spec__ is not set') from None
        else:
            if spec is None:
                raise ValueError(f'{name}.__spec__ is None')
            return spec
