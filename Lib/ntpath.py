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
    """Normalize a path, collapsing . and .. components.

    CPython 3.12: Lib/ntpath.py normpath (drive/UNC/root simplification).
    """
    path = str(path)
    if not path:
        return '.'
    path = path.replace(altsep, sep)

    # UNC prefix \\server\share
    if path.startswith(sep + sep) and not path.startswith(sep + sep + sep):
        prefix = sep + sep
        rest = path[2:]
        parts = [p for p in rest.split(sep) if p]
        server = parts[0] if parts else ''
        share = parts[1] if len(parts) > 1 else ''
        root = sep + sep + server + (sep + share if share else '')
        tail_parts = parts[2:]
    else:
        prefix = ''
        drive = ''
        rest = path
        if len(rest) >= 2 and rest[1] == ':':
            drive = rest[:2]
            rest = rest[2:]
        if drive and rest.startswith(sep):
            root = drive + sep
            tail_parts = rest[1:]
        elif drive:
            root = drive
            tail_parts = rest
        elif rest.startswith(sep):
            root = sep
            tail_parts = rest[1:]
        else:
            root = ''
            tail_parts = rest

    comps = []
    for piece in tail_parts.split(sep):
        if not piece or piece == '.':
            continue
        if piece == '..':
            if comps and comps[-1] != '..':
                comps.pop()
            elif not root:
                comps.append('..')
            # At a fixed root the '..' component is dropped.
            continue
        comps.append(piece)

    if root:
        if not comps:
            return root
        if root.endswith(sep):
            return root + sep.join(comps)
        return root + sep + sep.join(comps)
    return sep.join(comps) if comps else '.'


def splitdrive(p):
    """Split a pathname into drive/UNC sharepoint and relative path.

    CPython 3.12: Lib/ntpath.py splitdrive.
    """
    p = str(p)
    if len(p) >= 2 and p[1] == ':':
        return (p[:2], p[2:])
    if len(p) >= 2 and p[0] in (sep, altsep) and p[1] in (sep, altsep):
        q = p.replace(altsep, sep)
        if len(q) > 2 and q[2] not in (sep, altsep):
            i = q.find(sep, 2)
            if i == -1:
                return (p, '')
            j = q.find(sep, i + 1)
            if j == -1:
                return (p, '')
            return (p[:j], p[j:])
        return ('', p)
    return ('', p)

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


def islink(path):
    """Test whether a path is a symbolic link.

    Note: This is a simplified implementation. Since SharpPy doesn't have
    lstat yet, this always returns False (which is safe for most cases).
    CPython 3.12: Lib/genericpath.py:77-85
    """
    # Without lstat, we can't reliably detect symlinks on Windows
    # Safe fallback: return False
    return False


def isjunction(path):
    """Test whether a path is a junction.

    Note: This is a simplified implementation.
    CPython 3.12: Lib/ntpath.py:283-295
    """
    # Without st_reparse_tag support, we can't detect junctions
    return False


def lexists(path):
    """Test whether a path exists. Returns True for broken symbolic links.

    CPython 3.12: Lib/ntpath.py:300-306
    """
    # Without lstat, same as exists
    return exists(path)
