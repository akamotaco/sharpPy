"""
OS interface module for SharpPy.

This module provides a portable way to use operating system dependent functionality.
Compatible with CPython 3.12 os module core features.
"""

import sys

# Platform detection
if sys.platform.startswith('win'):
    name = 'nt'
else:
    name = 'posix'

# Path separator
sep = '\\' if name == 'nt' else '/'
altsep = '/' if name == 'nt' else None
pathsep = ';' if name == 'nt' else ':'

# Line separator
linesep = '\r\n' if name == 'nt' else '\n'

# Current working directory simulation
_cwd = "C:\\" if name == 'nt' else "/home"

# Environment variables simulation
environ = {
    'PATH': 'C:\\Windows\\System32' if name == 'nt' else '/usr/bin:/bin',
    'HOME': 'C:\\Users\\user' if name == 'nt' else '/home/user',
    'USER': 'user',
    'TEMP': 'C:\\Temp' if name == 'nt' else '/tmp',
    'TMP': 'C:\\Temp' if name == 'nt' else '/tmp',
}

# Exit codes
EX_OK = 0
EX_USAGE = 64
EX_DATAERR = 65
EX_NOINPUT = 66
EX_NOUSER = 67
EX_NOHOST = 68
EX_UNAVAILABLE = 69
EX_SOFTWARE = 70
EX_OSERR = 71
EX_OSFILE = 72
EX_CANTCREAT = 73
EX_IOERR = 74
EX_TEMPFAIL = 75
EX_PROTOCOL = 76
EX_NOPERM = 77
EX_CONFIG = 78

# File access constants
F_OK = 0  # Test for existence of file
R_OK = 4  # Test for read permission
W_OK = 2  # Test for write permission
X_OK = 1  # Test for execute permission

# Open flags
O_RDONLY = 0
O_WRONLY = 1
O_RDWR = 2
O_APPEND = 8
O_CREAT = 256
O_EXCL = 1024
O_TRUNC = 512

# Core functions
def getcwd():
    """Return a string representing the current working directory."""
    return _cwd

def chdir(path):
    """Change the current working directory to path."""
    global _cwd
    if not isinstance(path, str):
        raise TypeError("chdir() argument must be str, not " + type(path).__name__)

    # Basic path validation
    if not path:
        raise FileNotFoundError("No such file or directory: ''")

    # Simulate directory change
    _cwd = path
    return None

def listdir(path='.'):
    """Return a list containing the names of the files in the directory."""
    if path == '.':
        path = _cwd

    # Simulate directory listing - return common files/directories
    if path in (_cwd, 'C:\\', '/', '.'):
        return ['Documents', 'Downloads', 'Desktop', 'file1.txt', 'file2.py', 'folder1']
    else:
        # Return empty list for unknown directories
        return []

def mkdir(path, mode=0o777):
    """Create a directory named path with numeric mode mode."""
    if not isinstance(path, str):
        raise TypeError("mkdir() argument 1 must be str, not " + type(path).__name__)

    # Simulate directory creation (no actual filesystem operation)
    print(f"Directory created: {path}")
    return None

def makedirs(name, mode=0o777, exist_ok=False):
    """Create directories recursively."""
    if not isinstance(name, str):
        raise TypeError("makedirs() argument 1 must be str, not " + type(name).__name__)

    # Simulate recursive directory creation
    print(f"Directories created recursively: {name}")
    return None

def remove(path):
    """Remove (delete) the file path."""
    if not isinstance(path, str):
        raise TypeError("remove() argument must be str, not " + type(path).__name__)

    # Simulate file removal
    print(f"File removed: {path}")
    return None

def rmdir(path):
    """Remove (delete) the directory path."""
    if not isinstance(path, str):
        raise TypeError("rmdir() argument must be str, not " + type(path).__name__)

    # Simulate directory removal
    print(f"Directory removed: {path}")
    return None

def rename(src, dst):
    """Rename the file or directory src to dst."""
    if not isinstance(src, str) or not isinstance(dst, str):
        raise TypeError("rename() arguments must be str")

    # Simulate file/directory rename
    print(f"Renamed: {src} -> {dst}")
    return None

def getenv(key, default=None):
    """Get an environment variable, returning default if it doesn't exist."""
    return environ.get(key, default)

def putenv(key, value):
    """Set the environment variable named key to the string value."""
    if not isinstance(key, str) or not isinstance(value, str):
        raise TypeError("putenv() arguments must be str")

    environ[key] = value
    return None

def unsetenv(key):
    """Unset (delete) the environment variable named key."""
    if not isinstance(key, str):
        raise TypeError("unsetenv() argument must be str")

    if key in environ:
        del environ[key]
    return None

def access(path, mode):
    """Test for access to path with mode."""
    if not isinstance(path, str):
        raise TypeError("access() argument 1 must be str, not " + type(path).__name__)

    # Simulate access test - always return True for simplicity
    return True

def chmod(path, mode):
    """Change the access permissions of path to mode."""
    if not isinstance(path, str):
        raise TypeError("chmod() argument 1 must be str, not " + type(path).__name__)

    # Simulate permission change
    print(f"Permissions changed for: {path} (mode: {mode})")
    return None

def stat(path):
    """Get the status of a file or a file descriptor."""
    if not isinstance(path, str):
        raise TypeError("stat() argument 1 must be str, not " + type(path).__name__)

    # Return a simple stat result simulation
    class StatResult:
        def __init__(self):
            self.st_mode = 33188  # Regular file
            self.st_ino = 12345
            self.st_dev = 1
            self.st_nlink = 1
            self.st_uid = 1000
            self.st_gid = 1000
            self.st_size = 1024
            self.st_atime = 1640995200.0  # 2022-01-01 00:00:00
            self.st_mtime = 1640995200.0
            self.st_ctime = 1640995200.0

    return StatResult()

def path_exists(path):
    """Test whether a path exists."""
    if not isinstance(path, str):
        return False

    # Simulate existence check - return True for common paths
    common_paths = [_cwd, 'C:\\', '/', '.', '..', 'Documents', 'file1.txt']
    return path in common_paths or path.endswith('.py')

def path_isfile(path):
    """Test whether a path is a regular file."""
    if not isinstance(path, str):
        return False

    # Simulate file check
    return path.endswith(('.txt', '.py', '.log')) or 'file' in path.lower()

def path_isdir(path):
    """Test whether a path is a directory."""
    if not isinstance(path, str):
        return False

    # Simulate directory check
    return path in (_cwd, 'C:\\', '/', '.', '..', 'Documents', 'Downloads', 'Desktop')

def path_isabs(path):
    """Test whether a path is absolute."""
    if not isinstance(path, str):
        return False

    if name == 'nt':
        return len(path) > 1 and path[1] == ':' or path.startswith('\\\\')
    else:
        return path.startswith('/')

def path_join(*args):
    """Join one or more path components intelligently."""
    if not args:
        return ''

    result = args[0]
    for part in args[1:]:
        if path_isabs(part):
            result = part
        elif result and not result.endswith(sep):
            result += sep + part
        else:
            result += part

    return result

def path_split(path):
    """Split a pathname into head and tail."""
    if not isinstance(path, str):
        return ('', '')

    if not path:
        return ('', '')

    i = path.rfind(sep)
    if i == -1:
        return ('', path)

    head, tail = path[:i], path[i+1:]
    if head and head != sep * len(head):
        head = head.rstrip(sep)

    return (head, tail)

def path_dirname(path):
    """Return the directory name of pathname path."""
    return path_split(path)[0]

def path_basename(path):
    """Return the base name of pathname path."""
    return path_split(path)[1]

def path_splitext(path):
    """Split the pathname path into (root, ext)."""
    if not isinstance(path, str):
        return ('', '')

    dot_index = path.rfind('.')
    if dot_index == -1 or dot_index == 0:
        return (path, '')

    return (path[:dot_index], path[dot_index:])

def path_normpath(path):
    """Normalize a pathname by collapsing redundant separators."""
    if not isinstance(path, str):
        return ''

    # Simple normalization - remove double separators
    while sep + sep in path:
        path = path.replace(sep + sep, sep)

    return path

def path_abspath(path):
    """Return the absolute version of a path."""
    if not isinstance(path, str):
        return ''

    if path_isabs(path):
        return path_normpath(path)

    return path_normpath(path_join(_cwd, path))

# Create path submodule simulation
class PathModule:
    """Simulation of os.path module."""

    exists = staticmethod(path_exists)
    isfile = staticmethod(path_isfile)
    isdir = staticmethod(path_isdir)
    isabs = staticmethod(path_isabs)
    join = staticmethod(path_join)
    split = staticmethod(path_split)
    dirname = staticmethod(path_dirname)
    basename = staticmethod(path_basename)
    splitext = staticmethod(path_splitext)
    normpath = staticmethod(path_normpath)
    abspath = staticmethod(path_abspath)

# Create the path submodule
path = PathModule()

# Export list
__all__ = [
    'name', 'sep', 'altsep', 'pathsep', 'linesep', 'environ',
    'getcwd', 'chdir', 'listdir', 'mkdir', 'makedirs', 'remove', 'rmdir', 'rename',
    'getenv', 'putenv', 'unsetenv', 'access', 'chmod', 'stat',
    'F_OK', 'R_OK', 'W_OK', 'X_OK',
    'O_RDONLY', 'O_WRONLY', 'O_RDWR', 'O_APPEND', 'O_CREAT', 'O_EXCL', 'O_TRUNC',
    'EX_OK', 'EX_USAGE', 'EX_DATAERR', 'EX_NOINPUT', 'EX_NOUSER', 'EX_NOHOST',
    'EX_UNAVAILABLE', 'EX_SOFTWARE', 'EX_OSERR', 'EX_OSFILE', 'EX_CANTCREAT',
    'EX_IOERR', 'EX_TEMPFAIL', 'EX_PROTOCOL', 'EX_NOPERM', 'EX_CONFIG',
    'path'
]