"""
Input/output classes and functions for SharpPy.

This module provides the Python interfaces to stream handling.
Compatible with CPython 3.12 io module.
"""

# Import standard library modules
import abc

# Base classes
class IOBase(abc.ABC):
    """Base class for all I/O classes."""

    def __init__(self):
        self._closed = False

    @property
    def closed(self):
        """True if the stream is closed."""
        return self._closed

    def close(self):
        """Close the stream."""
        if not self._closed:
            self.flush()
            self._closed = True

    def __enter__(self):
        """Context manager entry."""
        return self

    def __exit__(self, type, value, traceback):
        """Context manager exit."""
        self.close()

    def flush(self):
        """Flush write buffers, if applicable."""
        pass

    def readable(self):
        """Return whether object supports reading."""
        return False

    def writable(self):
        """Return whether object supports writing."""
        return False

    def seekable(self):
        """Return whether object supports seeking."""
        return False


class RawIOBase(IOBase):
    """Base class for raw binary I/O."""

    def read(self, size=-1):
        """Read and return up to size bytes."""
        raise NotImplementedError

    def write(self, b):
        """Write bytes b to the stream."""
        raise NotImplementedError


class BufferedIOBase(IOBase):
    """Base class for buffered I/O."""

    def read(self, size=-1):
        """Read and return up to size bytes."""
        raise NotImplementedError

    def write(self, b):
        """Write bytes-like object b to the stream."""
        raise NotImplementedError


class TextIOBase(IOBase):
    """Base class for text I/O."""

    def read(self, size=-1):
        """Read and return at most size characters."""
        raise NotImplementedError

    def write(self, s):
        """Write string s to the stream."""
        raise NotImplementedError

    def readline(self, size=-1):
        """Read until newline or EOF."""
        raise NotImplementedError


# String I/O implementation
class StringIO(TextIOBase):
    """Text I/O implementation using an in-memory string buffer."""

    def __init__(self, initial_value=""):
        super().__init__()
        self._buffer = initial_value
        self._pos = 0

    def getvalue(self):
        """Return the entire contents of the StringIO."""
        return self._buffer

    def read(self, size=-1):
        """Read and return at most size characters."""
        if size is None or size < 0:
            result = self._buffer[self._pos:]
            self._pos = len(self._buffer)
        else:
            result = self._buffer[self._pos:self._pos + size]
            self._pos += len(result)
        return result

    def readline(self, size=-1):
        """Read until newline or EOF."""
        start = self._pos
        if size is None or size < 0:
            # Read until newline or end
            newline_pos = self._buffer.find('\n', start)
            if newline_pos == -1:
                result = self._buffer[start:]
                self._pos = len(self._buffer)
            else:
                result = self._buffer[start:newline_pos + 1]
                self._pos = newline_pos + 1
        else:
            # Read up to size characters or until newline
            end = min(start + size, len(self._buffer))
            newline_pos = self._buffer.find('\n', start, end)
            if newline_pos == -1:
                result = self._buffer[start:end]
                self._pos = end
            else:
                result = self._buffer[start:newline_pos + 1]
                self._pos = newline_pos + 1
        return result

    def write(self, s):
        """Write string s to the stream."""
        if not isinstance(s, str):
            raise TypeError("a str object is required")

        # Insert at current position, extending buffer if necessary
        before = self._buffer[:self._pos]
        after = self._buffer[self._pos + len(s):]
        self._buffer = before + s + after
        self._pos += len(s)
        return len(s)

    def seek(self, pos, whence=0):
        """Change stream position."""
        if whence == 0:  # SEEK_SET
            new_pos = pos
        elif whence == 1:  # SEEK_CUR
            new_pos = self._pos + pos
        elif whence == 2:  # SEEK_END
            new_pos = len(self._buffer) + pos
        else:
            raise ValueError("whence must be 0, 1, or 2")

        if new_pos < 0:
            new_pos = 0

        self._pos = new_pos
        return self._pos

    def tell(self):
        """Return current stream position."""
        return self._pos

    def readable(self):
        """Return whether object supports reading."""
        return True

    def writable(self):
        """Return whether object supports writing."""
        return True

    def seekable(self):
        """Return whether object supports seeking."""
        return True


# Bytes I/O implementation
class BytesIO(BufferedIOBase):
    """Buffered I/O implementation using an in-memory bytes buffer."""

    def __init__(self, initial_bytes=b""):
        super().__init__()
        self._buffer = bytearray(initial_bytes)
        self._pos = 0

    def getvalue(self):
        """Return the entire contents of the BytesIO."""
        return bytes(self._buffer)

    def read(self, size=-1):
        """Read and return at most size bytes."""
        if size is None or size < 0:
            result = self._buffer[self._pos:]
            self._pos = len(self._buffer)
        else:
            result = self._buffer[self._pos:self._pos + size]
            self._pos += len(result)
        return bytes(result)

    def write(self, b):
        """Write bytes-like object b to the stream."""
        if not isinstance(b, (bytes, bytearray)):
            raise TypeError("a bytes-like object is required")

        # Insert at current position, extending buffer if necessary
        data = bytearray(b)
        needed_len = self._pos + len(data)
        if needed_len > len(self._buffer):
            self._buffer.extend(b'\x00' * (needed_len - len(self._buffer)))

        self._buffer[self._pos:self._pos + len(data)] = data
        self._pos += len(data)
        return len(data)

    def seek(self, pos, whence=0):
        """Change stream position."""
        if whence == 0:  # SEEK_SET
            new_pos = pos
        elif whence == 1:  # SEEK_CUR
            new_pos = self._pos + pos
        elif whence == 2:  # SEEK_END
            new_pos = len(self._buffer) + pos
        else:
            raise ValueError("whence must be 0, 1, or 2")

        if new_pos < 0:
            new_pos = 0

        self._pos = new_pos
        return self._pos

    def tell(self):
        """Return current stream position."""
        return self._pos

    def readable(self):
        """Return whether object supports reading."""
        return True

    def writable(self):
        """Return whether object supports writing."""
        return True

    def seekable(self):
        """Return whether object supports seeking."""
        return True


# Constants
SEEK_SET = 0
SEEK_CUR = 1
SEEK_END = 2

# Default buffer size
DEFAULT_BUFFER_SIZE = 8192

# Expose the main classes
__all__ = [
    'IOBase', 'RawIOBase', 'BufferedIOBase', 'TextIOBase',
    'StringIO', 'BytesIO',
    'SEEK_SET', 'SEEK_CUR', 'SEEK_END',
    'DEFAULT_BUFFER_SIZE'
]