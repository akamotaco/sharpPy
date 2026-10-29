# "from module import name" failures are ImportError in CPython, not AttributeError.
# ceval.c import_from() answers:
#   cannot import name 'NOPE' from 'os' (C:\...\Lib\os.py)
#   cannot import name 'NOPE' from 'sys' (unknown location)
# SharpPy used to leak the AttributeError out of the lookup, which also broke the standard
# idiom of probing an optional name with try: from _io import _WindowsConsoleIO / except
# ImportError. The parenthesised origin is the module's source path for source modules and
# "unknown location" for builtin and extension modules, so placeholders such as "_io.py" or
# "<builtin sys module>" must not be reported as a path. Normalised here to (PATH).
import io


def norm(msg):
    at = msg.rfind(" (")
    if at > 0 and msg.endswith(".py)"):
        return msg[:at] + " (PATH)"
    return msg


try:
    from _io import NOPE
    print("missing_native ok")
except ImportError as exc:
    print("missing_native", type(exc).__name__, norm(str(exc)))

try:
    from sys import NOPE
    print("missing_builtin ok")
except ImportError as exc:
    print("missing_builtin", type(exc).__name__, norm(str(exc)))

try:
    from os import NOPE
    print("missing_source ok")
except ImportError as exc:
    print("missing_source", type(exc).__name__, norm(str(exc)))

try:
    from json import NOPE
    print("missing_pkg ok")
except ImportError as exc:
    print("missing_pkg", type(exc).__name__, norm(str(exc)))

# The optional-name idiom has to be catchable. _WindowsConsoleIO itself is a real gap (it exists
# in CPython on Windows and is not implemented here), so the case below uses a name neither engine
# has, which keeps the check about catchability instead of about that missing type.
try:
    from _io import _NoSuchConsoleIO
    print("optional ok")
except ImportError as exc:
    print("optional caught", type(exc).__name__)

# Real names still resolve, including a submodule binding.
from os import sep
from collections import OrderedDict
print("real_names", bool(sep), OrderedDict.__name__)

# open is one object in CPython: _io.open is what builtins.open is set to.
import builtins
print("open_identity", io.open is builtins.open)
print("io_open_type", type(io.open).__name__)
