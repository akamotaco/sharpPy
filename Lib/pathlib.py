"""
Object-oriented filesystem paths

This module provides classes representing filesystem paths with semantics
appropriate for different operating systems.
"""

import os
import sys
import stat
from typing import Union, Optional, Iterator, List, Tuple, Any


class PurePath:
    """Base class for manipulating paths without I/O."""

    def __init__(self, *args):
        """Initialize a pure path."""
        if not args:
            # Current directory
            parts = ['.']
        else:
            # Convert all arguments to strings and join them
            parts = []
            for arg in args:
                if isinstance(arg, PurePath):
                    parts.extend(arg.parts)
                else:
                    parts.append(str(arg))

        # Normalize the path
        self._parts = self._normalize_parts(parts)

    @classmethod
    def _normalize_parts(cls, parts):
        """Normalize path parts."""
        if not parts:
            return ['.']

        # Join all parts and split by separator
        joined = '/'.join(str(p) for p in parts)
        if os.name == 'nt':
            joined = joined.replace('/', '\\')

        # Handle absolute vs relative paths
        if os.path.isabs(joined):
            normalized = os.path.normpath(joined)
        else:
            normalized = os.path.normpath(joined)

        # Split into parts
        if os.name == 'nt':
            # Windows path handling
            if '\\' in normalized:
                result = normalized.split('\\')
            else:
                result = [normalized]
        else:
            # POSIX path handling
            if '/' in normalized:
                result = normalized.split('/')
            else:
                result = [normalized]

        # Remove empty parts except for root
        if result and result[0] == '':
            result = ['/'] + [p for p in result[1:] if p]
        else:
            result = [p for p in result if p]

        return result or ['.']

    @property
    def parts(self):
        """Return a tuple of path parts."""
        return tuple(self._parts)

    @property
    def name(self):
        """The final component of the path."""
        return self._parts[-1] if self._parts and self._parts != ['.'] else ''

    @property
    def suffix(self):
        """The file extension of the final component."""
        name = self.name
        if '.' in name and not name.startswith('.'):
            return '.' + name.split('.')[-1]
        return ''

    @property
    def suffixes(self):
        """A list of suffixes of the final component."""
        name = self.name
        if '.' not in name or name.startswith('.'):
            return []
        parts = name.split('.')
        return ['.' + suffix for suffix in parts[1:]]

    @property
    def stem(self):
        """The final component without its suffix."""
        name = self.name
        if '.' in name and not name.startswith('.'):
            return name.rsplit('.', 1)[0]
        return name

    @property
    def parent(self):
        """The logical parent of this path."""
        if len(self._parts) <= 1:
            return self.__class__('.')
        return self.__class__(*self._parts[:-1])

    @property
    def parents(self):
        """A sequence of this path's logical parents."""
        parents = []
        current = self.parent
        while current != current.parent:
            parents.append(current)
            current = current.parent
        return parents

    def __str__(self):
        """Return the string representation of the path."""
        if self._parts == ['.']:
            return '.'

        if os.name == 'nt':
            # Windows path
            if self._parts[0] == '/':
                # Absolute path converted to Windows
                return '\\'.join(self._parts[1:])
            elif len(self._parts[0]) == 2 and self._parts[0][1] == ':':
                # Drive letter
                return '\\'.join(self._parts)
            else:
                # Relative path
                return '\\'.join(self._parts)
        else:
            # POSIX path
            if self._parts[0] == '/':
                return '/' + '/'.join(self._parts[1:])
            else:
                return '/'.join(self._parts)

    def __repr__(self):
        return f"{self.__class__.__name__}({str(self)!r})"

    def __truediv__(self, key):
        """Join paths using the / operator."""
        return self.__class__(self, key)

    def __eq__(self, other):
        """Check equality with another path."""
        if not isinstance(other, PurePath):
            return NotImplemented
        return self._parts == other._parts

    def __hash__(self):
        """Return hash of the path."""
        return hash(tuple(self._parts))

    def joinpath(self, *args):
        """Join paths."""
        return self.__class__(self, *args)

    def match(self, pattern):
        """Test whether this path matches a glob pattern."""
        import fnmatch
        return fnmatch.fnmatch(str(self), pattern)

    def with_name(self, name):
        """Return a new path with the name changed."""
        if not self.name:
            raise ValueError("Empty name cannot be replaced")
        return self.parent / name

    def with_suffix(self, suffix):
        """Return a new path with the suffix changed."""
        if not suffix.startswith('.'):
            raise ValueError("Invalid suffix")
        return self.with_name(self.stem + suffix)

    def is_absolute(self):
        """Return True if the path is absolute."""
        return os.path.isabs(str(self))

    def is_relative_to(self, other):
        """Return True if this path is relative to another path."""
        try:
            self.relative_to(other)
            return True
        except ValueError:
            return False

    def relative_to(self, other):
        """Return a relative path from other to this path."""
        other = self.__class__(other)
        if not self.is_absolute() == other.is_absolute():
            raise ValueError("Cannot mix absolute and relative paths")

        self_parts = list(self._parts)
        other_parts = list(other._parts)

        # Find common prefix
        common_len = 0
        for i, (a, b) in enumerate(zip(self_parts, other_parts)):
            if a == b:
                common_len = i + 1
            else:
                break

        if common_len == 0:
            raise ValueError(f"{str(self)!r} is not relative to {str(other)!r}")

        # Return the remaining parts
        remaining = self_parts[common_len:]
        return self.__class__(*remaining) if remaining else self.__class__('.')


class Path(PurePath):
    """Concrete path class that can perform I/O operations."""

    def __new__(cls, *args):
        """Create a new Path instance."""
        if cls is Path:
            # Return platform-specific subclass
            if os.name == 'nt':
                return WindowsPath(*args)
            else:
                return PosixPath(*args)
        return super().__new__(cls)

    def stat(self):
        """Get stat information for this path."""
        return os.stat(str(self))

    def lstat(self):
        """Get lstat information for this path."""
        return os.lstat(str(self))

    def exists(self):
        """Return True if the path exists."""
        try:
            self.stat()
            return True
        except (OSError, ValueError):
            return False

    def is_dir(self):
        """Return True if this path is a directory."""
        try:
            return stat.S_ISDIR(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def is_file(self):
        """Return True if this path is a regular file."""
        try:
            return stat.S_ISREG(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def is_mount(self):
        """Return True if this path is a mount point."""
        return os.path.ismount(str(self))

    def is_symlink(self):
        """Return True if this path is a symbolic link."""
        try:
            return stat.S_ISLNK(self.lstat().st_mode)
        except (OSError, ValueError):
            return False

    def is_socket(self):
        """Return True if this path is a socket."""
        try:
            return stat.S_ISSOCK(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def is_fifo(self):
        """Return True if this path is a FIFO."""
        try:
            return stat.S_ISFIFO(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def is_block_device(self):
        """Return True if this path is a block device."""
        try:
            return stat.S_ISBLK(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def is_char_device(self):
        """Return True if this path is a character device."""
        try:
            return stat.S_ISCHR(self.stat().st_mode)
        except (OSError, ValueError):
            return False

    def iterdir(self):
        """Iterate over the contents of a directory."""
        for name in os.listdir(str(self)):
            yield self / name

    def glob(self, pattern):
        """Find all paths matching a glob pattern."""
        import glob
        for path in glob.glob(os.path.join(str(self), pattern)):
            yield self.__class__(path)

    def rglob(self, pattern):
        """Find all paths matching a glob pattern recursively."""
        import glob
        for path in glob.glob(os.path.join(str(self), '**', pattern), recursive=True):
            yield self.__class__(path)

    def absolute(self):
        """Return an absolute version of this path."""
        return self.__class__(os.path.abspath(str(self)))

    def resolve(self, strict=False):
        """Resolve symbolic links and return the absolute path."""
        try:
            return self.__class__(os.path.realpath(str(self)))
        except OSError:
            if strict:
                raise
            return self.absolute()

    def expanduser(self):
        """Expand ~ and ~user constructions."""
        return self.__class__(os.path.expanduser(str(self)))

    def home(cls):
        """Return the home directory."""
        return cls(os.path.expanduser('~'))

    def cwd(cls):
        """Return the current working directory."""
        return cls(os.getcwd())

    @classmethod
    def home(cls):
        """Return the home directory."""
        return cls(os.path.expanduser('~'))

    @classmethod
    def cwd(cls):
        """Return the current working directory."""
        return cls(os.getcwd())

    def mkdir(self, mode=0o777, parents=False, exist_ok=False):
        """Create a directory."""
        try:
            if parents:
                os.makedirs(str(self), mode, exist_ok=exist_ok)
            else:
                os.mkdir(str(self), mode)
        except FileExistsError:
            if not exist_ok:
                raise

    def rmdir(self):
        """Remove the directory."""
        os.rmdir(str(self))

    def unlink(self, missing_ok=False):
        """Remove the file."""
        try:
            os.unlink(str(self))
        except FileNotFoundError:
            if not missing_ok:
                raise

    def rename(self, target):
        """Rename the file or directory."""
        os.rename(str(self), str(target))
        return self.__class__(target)

    def replace(self, target):
        """Replace the file or directory."""
        os.replace(str(self), str(target))
        return self.__class__(target)

    def symlink_to(self, target, target_is_directory=False):
        """Create a symbolic link pointing to target."""
        os.symlink(str(target), str(self), target_is_directory)

    def hardlink_to(self, target):
        """Create a hard link pointing to target."""
        os.link(str(target), str(self))

    def chmod(self, mode):
        """Change the permissions of the path."""
        os.chmod(str(self), mode)

    def touch(self, mode=0o666, exist_ok=True):
        """Create a file at this path."""
        if self.exists():
            if exist_ok:
                # Update access and modification times
                os.utime(str(self), None)
                return
            else:
                raise FileExistsError(str(self))

        # Create the file
        with open(str(self), 'a'):
            pass
        self.chmod(mode)

    def open(self, mode='r', buffering=-1, encoding=None, errors=None, newline=None):
        """Open the file."""
        return open(str(self), mode, buffering, encoding, errors, newline)

    def read_text(self, encoding=None, errors=None):
        """Read text from the file."""
        with self.open('r', encoding=encoding, errors=errors) as f:
            return f.read()

    def read_bytes(self):
        """Read bytes from the file."""
        with self.open('rb') as f:
            return f.read()

    def write_text(self, data, encoding=None, errors=None, newline=None):
        """Write text to the file."""
        with self.open('w', encoding=encoding, errors=errors, newline=newline) as f:
            return f.write(data)

    def write_bytes(self, data):
        """Write bytes to the file."""
        with self.open('wb') as f:
            return f.write(data)


class PosixPath(Path):
    """Path class for POSIX systems."""
    pass


class WindowsPath(Path):
    """Path class for Windows systems."""
    pass


# Platform-specific path classes
if os.name == 'nt':
    # Windows
    class PurePosixPath(PurePath):
        """Pure path class for POSIX systems (not available on Windows)."""
        def __new__(cls, *args):
            raise NotImplementedError("PurePosixPath is not available on Windows")

    class PureWindowsPath(PurePath):
        """Pure path class for Windows systems."""
        pass

else:
    # POSIX
    class PurePosixPath(PurePath):
        """Pure path class for POSIX systems."""
        pass

    class PureWindowsPath(PurePath):
        """Pure path class for Windows systems (not available on POSIX)."""
        def __new__(cls, *args):
            raise NotImplementedError("PureWindowsPath is not available on POSIX systems")


# Export public API
__all__ = [
    'PurePath', 'Path',
    'PurePosixPath', 'PureWindowsPath',
    'PosixPath', 'WindowsPath'
]