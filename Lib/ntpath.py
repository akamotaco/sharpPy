# SharpPy ntpath module - Windows path manipulation
# Simplified implementation for SharpPy

import nt as _nt

# Path separator
sep = '\\'
altsep = '/'
extsep = '.'
pathsep = ';'
defpath = '.;C:\\bin'

def join(path, *paths):
    """Join one or more path components."""
    result = str(path)
    for p in paths:
        p = str(p)
        if p.startswith(sep) or p.startswith(altsep) or (len(p) > 1 and p[1] == ':'):
            result = p
        elif not result or result.endswith(sep) or result.endswith(altsep):
            result = result + p
        else:
            result = result + sep + p
    return result

def split(path):
    """Split a path into directory and basename."""
    path = str(path)
    # Find last separator
    i = max(path.rfind(sep), path.rfind(altsep))
    if i < 0:
        return ('', path)
    head = path[:i]
    tail = path[i+1:]
    # Remove trailing slashes from head (but keep root)
    while head and (head.endswith(sep) or head.endswith(altsep)):
        if len(head) == 1 or (len(head) == 3 and head[1] == ':'):
            break
        head = head[:-1]
    return (head, tail)

def dirname(path):
    """Return the directory component of a pathname."""
    return split(path)[0]

def basename(path):
    """Return the final component of a pathname."""
    return split(path)[1]

def exists(path):
    """Test whether a path exists."""
    try:
        return _nt.stat(path) is not None
    except:
        return False

def isfile(path):
    """Test whether a path is a regular file."""
    try:
        st = _nt.stat(path)
        return st.st_mode & 0o170000 == 0o100000  # S_ISREG
    except:
        return False

def isdir(path):
    """Test whether a path is a directory."""
    try:
        st = _nt.stat(path)
        return st.st_mode & 0o170000 == 0o040000  # S_ISDIR
    except:
        return False

def abspath(path):
    """Return an absolute path."""
    path = str(path)
    if not path:
        return _nt.getcwd()
    # Check if already absolute
    if len(path) >= 2 and path[1] == ':':
        # Has drive letter
        if len(path) == 2 or path[2] not in (sep, altsep):
            # Need to add current directory of that drive
            return join(_nt.getcwd(), path)
        return path
    if path.startswith(sep) or path.startswith(altsep):
        # UNC path or root-relative
        return join(_nt.getcwd()[:2], path)
    return join(_nt.getcwd(), path)

def normpath(path):
    """Normalize a path."""
    path = str(path)
    # Replace alt separators
    path = path.replace(altsep, sep)
    # Remove redundant separators
    while sep + sep in path:
        path = path.replace(sep + sep, sep)
    return path

def splitext(path):
    """Split the extension from a pathname."""
    path = str(path)
    sep_idx = max(path.rfind(sep), path.rfind(altsep))
    dot_idx = path.rfind(extsep)
    if dot_idx > sep_idx + 1:
        return (path[:dot_idx], path[dot_idx:])
    return (path, '')

def getsize(path):
    """Return the size of a file, reported by stat."""
    return _nt.stat(path).st_size

def isabs(path):
    """Test whether a path is absolute."""
    path = str(path)
    if len(path) >= 1 and path[0] in (sep, altsep):
        return True
    if len(path) >= 2 and path[1] == ':':
        return True
    return False

def expanduser(path):
    """Expand ~ and ~user constructs."""
    path = str(path)
    if not path.startswith('~'):
        return path
    # Get home directory
    home = _nt.getenv('USERPROFILE') or _nt.getenv('HOME') or ''
    if path == '~':
        return home
    if path.startswith('~' + sep) or path.startswith('~' + altsep):
        return home + path[1:]
    return path  # ~user not supported
