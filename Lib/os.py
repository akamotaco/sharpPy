"""OS routines for NT

CPython 3.12: Lib/os.py
This module provides a simplified version of os module for SharpPy.
"""

import sys
import nt
from _collections_abc import _check_methods

# os.path submodule - CPython 3.12: Lib/os.py:95
import ntpath as path
sys.modules['os.path'] = path

# Re-export commonly used functions from nt
# CPython 3.12: Lib/os.py:65-90
stat = nt.stat
getcwd = nt.getcwd
chdir = nt.chdir
listdir = nt.listdir
mkdir = nt.mkdir
rmdir = nt.rmdir
remove = nt.remove
unlink = nt.unlink
rename = nt.rename
access = nt.access
getenv = nt.getenv
putenv = nt.putenv
unsetenv = getattr(nt, 'unsetenv', None)
getpid = getattr(nt, 'getpid', None)
urandom = nt.urandom


class _Environ:
    """os.environ mapping that keeps the CPython observable type.

    CPython 3.12: Lib/os.py _Environ (MutableMapping) simplified for SharpPy;
    keys/values are plain str here.
    """

    def __init__(self, data):
        self._data = data

    def __setitem__(self, key, value):
        if not isinstance(key, str) or not isinstance(value, str):
            raise TypeError("str expected, not %s" % type(value).__name__)
        nt.putenv(key, value)
        self._data[key] = value

    def __getitem__(self, key):
        return self._data[key]

    def __delitem__(self, key):
        del self._data[key]
        try:
            nt.unsetenv(key)
        except AttributeError:
            pass

    def __iter__(self):
        yield from list(self._data)

    def __len__(self):
        return len(self._data)

    def __contains__(self, key):
        return key in self._data

    def copy(self):
        return dict(self._data)

    def keys(self):
        return list(self._data)

    def values(self):
        return [self._data[k] for k in self._data]

    def items(self):
        return [(k, self._data[k]) for k in self._data]

    def get(self, key, default=None):
        try:
            return self._data[key]
        except KeyError:
            return default

    def setdefault(self, key, default=None):
        if key not in self._data:
            self._data[key] = default
            try:
                nt.putenv(key, default)
            except Exception:
                pass
        return self._data[key]

    def pop(self, key, *args):
        try:
            value = self._data.pop(key)
        except KeyError:
            if args:
                return args[0]
            raise
        try:
            nt.unsetenv(key)
        except AttributeError:
            pass
        return value

    def __repr__(self):
        return "_Environ(%s)" % dict(self._data).__repr__()


environ = _Environ(nt.environ)


# fspath implementation
# CPython 3.12: Lib/os.py:1071-1098

def fspath(path):
    """Return the path representation of a path-like object.

    If str or bytes is passed in, it is returned unchanged. Otherwise the
    os.PathLike interface is used to get the path representation. If the
    path representation is not str or bytes, TypeError is raised. If the
    provided path is not str, bytes, or os.PathLike, TypeError is raised.

    CPython 3.12: Lib/os.py:1071-1098
    """
    if isinstance(path, (str, bytes)):
        return path

    # Work from the object's type to match method resolution of other magic
    # methods.
    path_type = type(path)
    try:
        path_repr = path_type.__fspath__(path)
    except AttributeError:
        if hasattr(path_type, '__fspath__'):
            raise
        else:
            raise TypeError("expected str, bytes or os.PathLike object, "
                            "not " + path_type.__name__)
    if isinstance(path_repr, (str, bytes)):
        return path_repr
    else:
        raise TypeError("expected {}.__fspath__() to return str or bytes, "
                        "not {}".format(path_type.__name__,
                                        type(path_repr).__name__))

# Access mode constants
# CPython 3.12: Lib/os.py:67-70
F_OK = nt.F_OK
R_OK = nt.R_OK
W_OK = nt.W_OK
X_OK = nt.X_OK

# Platform information
# CPython 3.12: Lib/os.py:41-52
name = 'nt'  # Windows

# Current and parent directory constants
# CPython 3.12: Lib/os.py:47-48
curdir = '.'
pardir = '..'

# Path separator constants
# CPython 3.12: Lib/os.py:49-52
sep = '\\'
altsep = '/'
extsep = '.'
pathsep = ';'
defpath = '.;C:\\bin'
devnull = 'nul'
linesep = '\r\n'


# Super-mkdir for creating directories with intermediate paths
# CPython 3.12: Lib/os.py:200-230

def makedirs(name, mode=0o777, exist_ok=False):
    """makedirs(name [, mode=0o777][, exist_ok=False])

    Super-mkdir; create a leaf directory and all intermediate ones.  Works like
    mkdir, except that any intermediate path segment (not just the rightmost)
    will be created if it does not exist. If the target directory already
    exists, raise an OSError if exist_ok is False. Otherwise no exception is
    raised.  This is recursive.

    CPython 3.12: Lib/os.py:200-230
    """
    head, tail = path.split(name)
    if not tail:
        head, tail = path.split(head)
    if head and tail and not path.exists(head):
        try:
            makedirs(head, exist_ok=exist_ok)
        except FileExistsError:
            # Defeats race condition when another thread created the path
            pass
        cdir = curdir
        if isinstance(tail, bytes):
            cdir = bytes(curdir, 'ASCII')
        if tail == cdir:           # xxx/newdir/. exists if xxx/newdir exists
            return
    try:
        mkdir(name, mode)
    except OSError:
        # Cannot rely on checking for EEXIST, since the operating system
        # could give priority to other errors like EACCES or EROFS
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

    CPython 3.12: Lib/os.py:232-252
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

    CPython 3.12: Lib/os.py:254-278
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


# Directory tree generator
# CPython 3.12: Lib/os.py:286-439
# Note: This is a simplified implementation using listdir instead of scandir

def walk(top, topdown=True, onerror=None, followlinks=False):
    """Directory tree generator.

    For each directory in the directory tree rooted at top (including top
    itself, but excluding '.' and '..'), yields a 3-tuple

        dirpath, dirnames, filenames

    dirpath is a string, the path to the directory.  dirnames is a list of
    the names of the subdirectories in dirpath (including symlinks to directories,
    and excluding '.' and '..').
    filenames is a list of the names of the non-directory files in dirpath.
    Note that the names in the lists are just names, with no path components.
    To get a full path (which begins with top) to a file or directory in
    dirpath, do os.path.join(dirpath, name).

    CPython 3.12: Lib/os.py:286-439 (simplified)
    """
    top = fspath(top)
    dirs = []
    nondirs = []

    # We may not have read permission for top, in which case we can't
    # get a list of the files the directory contains.
    try:
        names = listdir(top)
    except OSError as error:
        if onerror is not None:
            onerror(error)
        return

    for name in names:
        fullpath = path.join(top, name)
        try:
            is_dir = path.isdir(fullpath)
        except OSError:
            is_dir = False

        if is_dir:
            dirs.append(name)
        else:
            nondirs.append(name)

    if topdown:
        # Top-down: yield parent first, then recurse into children
        yield top, dirs, nondirs
        for dirname in dirs:
            new_path = path.join(top, dirname)
            if followlinks or not path.islink(new_path):
                yield from walk(new_path, topdown, onerror, followlinks)
    else:
        # Bottom-up: recurse into children first, then yield parent
        for dirname in dirs:
            new_path = path.join(top, dirname)
            if followlinks or not path.islink(new_path):
                yield from walk(new_path, topdown, onerror, followlinks)
        yield top, dirs, nondirs


# File system encoding/decoding
# CPython 3.12: Lib/os.py:841-864

def fsencode(filename):
    """Encode filename (an os.PathLike, bytes, or str) to the filesystem
    encoding with 'surrogateescape' error handler, return bytes unchanged.
    On Windows, use 'strict' error handler if the file system encoding is
    'mbcs' (which is the default encoding).

    CPython 3.12: Lib/os.py:841-851
    """
    filename = fspath(filename)  # Does type-checking of `filename`.
    if isinstance(filename, str):
        return filename.encode(sys.getfilesystemencoding(), 'surrogateescape')
    else:
        return filename


def fsdecode(filename):
    """Decode filename (an os.PathLike, bytes, or str) from the filesystem
    encoding with 'surrogateescape' error handler, return str unchanged.
    On Windows, use 'strict' error handler if the file system encoding is
    'mbcs' (which is the default encoding).

    CPython 3.12: Lib/os.py:853-864
    """
    filename = fspath(filename)  # Does type-checking of `filename`.
    if isinstance(filename, bytes):
        return filename.decode(sys.getfilesystemencoding(), 'surrogateescape')
    else:
        return filename


# PathLike ABC for objects representing a file system path
# CPython 3.12: Lib/os.py:1107-1122

class PathLike:
    """Abstract base class for implementing the file system path protocol.

    CPython 3.12: Lib/os.py:1107-1122
    """

    def __fspath__(self):
        """Return the file system path representation of the object."""
        raise NotImplementedError

    @classmethod
    def __subclasshook__(cls, subclass):
        if cls is PathLike:
            return _check_methods(subclass, '__fspath__')
        return NotImplemented
