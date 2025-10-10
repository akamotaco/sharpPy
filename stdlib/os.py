"""OS routines for NT or Posix depending on what system we're on.

This exports:
  - all functions from posix or nt, e.g. unlink, stat, etc.
  - os.path is ntpath (SharpPy currently supports Windows primarily)
  - os.name is either 'posix' or 'nt'
  - os.curdir is a string representing the current directory (always '.')
  - os.pardir is a string representing the parent directory (always '..')
  - os.sep is the (or a most common) pathname separator ('/' or '\\')
  - os.extsep is the extension separator (always '.')
  - os.altsep is the alternate pathname separator (None or '/')
  - os.pathsep is the component separator used in $PATH etc
  - os.linesep is the line separator in text files ('\r' or '\n' or '\r\n')
"""

import sys

# Determine which OS-specific module to import
# In SharpPy, 'nt' module is cross-platform (implemented in C#)
_names = sys.builtin_module_names

# Import OS-specific module
# Note: Windows has priority over posix
if 'nt' in _names:
    name = 'nt'
    linesep = '\r\n'
    import nt
    # Import core functions from nt
    getcwd = nt.getcwd
    chdir = nt.chdir
    listdir = nt.listdir
    mkdir = nt.mkdir
    rmdir = nt.rmdir
    remove = nt.remove
    unlink = nt.unlink
    rename = nt.rename
    stat = nt.stat
    access = nt.access
    getenv = nt.getenv
    putenv = nt.putenv
    environ = nt.environ
    fspath = nt.fspath
elif 'posix' in _names:
    name = 'posix'
    linesep = '\n'
    import posix
    # Import core functions from posix
    getcwd = posix.getcwd
    chdir = posix.chdir
    listdir = posix.listdir
    mkdir = posix.mkdir
    rmdir = posix.rmdir
    remove = posix.remove
    unlink = posix.unlink
    rename = posix.rename
    stat = posix.stat
    access = posix.access
    getenv = posix.getenv
    putenv = posix.putenv
    environ = posix.environ
    fspath = posix.fspath
else:
    raise ImportError('no os specific module found')

# Path separators (CPython 3.12 compatible)
curdir = '.'
pardir = '..'

if name == 'nt':
    sep = '\\'
    altsep = '/'
    pathsep = ';'
    extsep = '.'
    devnull = 'nul'
else:
    sep = '/'
    altsep = None
    pathsep = ':'
    extsep = '.'
    devnull = '/dev/null'

# Additional utility functions
def makedirs(name, mode=0o777, exist_ok=False):
    """makedirs(name [, mode=0o777][, exist_ok=False])

    Super-mkdir; create a leaf directory and all intermediate ones.  Works like
    mkdir, except that any intermediate path segment (not just the rightmost)
    will be created if it does not exist. If the target directory already
    exists, raise an OSError if exist_ok is False. Otherwise no exception is
    raised.  This is recursive.
    """
    head, tail = path.split(name)
    if not tail:
        head, tail = path.split(head)
    if head and tail and not path.exists(head):
        try:
            makedirs(head, mode, exist_ok)
        except FileExistsError:
            pass
        cdir = curdir
        if isinstance(tail, bytes):
            cdir = bytes(curdir, 'ASCII')
        if tail == cdir:
            return
    try:
        mkdir(name, mode)
    except OSError:
        if not exist_ok or not path.isdir(name):
            raise

def removedirs(name):
    """removedirs(name)

    Super-rmdir; remove a leaf directory and all empty intermediate
    ones.  Works like rmdir except that, if the leaf directory is
    successfully removed, directories corresponding to rightmost path
    segments will be pruned away until either the whole path is
    consumed or an error occurs.  Errors during this latter phase are
    ignored -- they generally mean that a directory was not empty.
    """
    rmdir(name)
    head, tail = path.split(name)
    if not tail:
        head, tail = path.split(head)
    while head and tail:
        try:
            rmdir(head)
        except OSError:
            break
        head, tail = path.split(head)

def renames(old, new):
    """renames(old, new)

    Super-rename; create directories as necessary and delete any left
    empty.  Works like rename, except creation of any intermediate
    directories needed to make the new pathname good is attempted
    first.  After the rename, directories corresponding to rightmost
    path segments of the old name will be pruned until either the
    whole path is consumed or a nonempty directory is found.

    Note: this function can fail with the new directory structure made
    if you lack permissions needed to unlink the leaf directory or
    file.
    """
    head, tail = path.split(new)
    if head and tail and not path.exists(head):
        makedirs(head)
    rename(old, new)
    head, tail = path.split(old)
    if head and tail:
        try:
            removedirs(head)
        except OSError:
            pass

# Minimal path support (full implementation would import ntpath/posixpath)
class path:
    """Minimal os.path implementation for SharpPy"""

    @staticmethod
    def split(p):
        """Split a pathname into directory and filename"""
        i = p.rfind(sep) + 1
        head, tail = p[:i], p[i:]
        if head and head != sep * len(head):
            head = head.rstrip(sep)
        return head, tail

    @staticmethod
    def exists(path_str):
        """Test whether a path exists"""
        try:
            stat(path_str)
            return True
        except:
            return False

    @staticmethod
    def isdir(path_str):
        """Return True if path is an existing directory"""
        try:
            st = stat(path_str)
            # Check if st_mode indicates directory (simplified)
            mode = st.get('st_mode', 0) if hasattr(st, 'get') else getattr(st, 'st_mode', 0)
            return (mode & 0x4000) != 0  # S_IFDIR
        except:
            return False

    @staticmethod
    def isfile(path_str):
        """Return True if path is an existing regular file"""
        try:
            st = stat(path_str)
            mode = st.get('st_mode', 0) if hasattr(st, 'get') else getattr(st, 'st_mode', 0)
            return (mode & 0x8000) != 0  # S_IFREG
        except:
            return False

    @staticmethod
    def join(a, *paths):
        """Join path components intelligently"""
        path_result = a
        for b in paths:
            if b.startswith(sep):
                path_result = b
            elif not path_result or path_result.endswith(sep):
                path_result += b
            else:
                path_result += sep + b
        return path_result

    @staticmethod
    def abspath(path_str):
        """Return absolute path"""
        if not path_str:
            path_str = getcwd()
        # Simplified: assume path is already absolute or relative to cwd
        if path_str.startswith(sep) or (len(path_str) > 1 and path_str[1] == ':'):
            return path_str
        return path.join(getcwd(), path_str)

    @staticmethod
    def dirname(p):
        """Returns the directory component of a pathname"""
        i = p.rfind(sep) + 1
        head = p[:i]
        if head and head != sep * len(head):
            head = head.rstrip(sep)
        return head

    @staticmethod
    def basename(p):
        """Returns the final component of a pathname"""
        i = p.rfind(sep) + 1
        return p[i:]
