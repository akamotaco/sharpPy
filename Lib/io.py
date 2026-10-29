"""
Input/output classes and functions for SharpPy.

This module provides the Python interfaces to stream handling.
Compatible with CPython 3.12 io module.
"""

# Import standard library modules
import abc

# CPython 3.12: Lib/io.py re-exports the concrete stream classes implemented in the
# native _io module (Modules/_io). SharpPy provides _io natively (the
# PyFileContextManager file types + UnsupportedOperation), so importing the exact
# type objects here keeps isinstance(open(f), io.TextIOWrapper) true and gives the
# "_io" __module__ in reprs.
from _io import (
    StringIO, TextIOWrapper, BufferedReader, BufferedWriter, BufferedRWPair,
    BufferedRandom,
    FileIO, IncrementalNewlineDecoder, UnsupportedOperation, text_encoding, open_code,
)
from builtins import BlockingIOError, open

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
# StringIO is the native _io.StringIO (core/PyStringIO.cs, the counterpart of
# Modules/_io/stringio.c). The python stand-in that used to live here is gone: under the
# library rule a C-only implementation is replaced in C#, and the native type is also what
# gives StringIO its _TextIOBase -> _IOBase chain and its real _io identity.
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


# CPython reports this concrete in-memory type as belonging to _io.
BytesIO.__module__ = '_io'


# Constants
SEEK_SET = 0
SEEK_CUR = 1
SEEK_END = 2

# Default buffer size
DEFAULT_BUFFER_SIZE = 8192

# CPython 3.12: exact io.__all__ ordering (Lib/io.py).
__all__ = ["BlockingIOError", "open", "open_code", "IOBase", "RawIOBase", "FileIO",
           "BytesIO", "StringIO", "BufferedIOBase", "BufferedReader",
           "BufferedWriter", "BufferedRWPair", "BufferedRandom",
           "TextIOBase", "TextIOWrapper", "UnsupportedOperation", "SEEK_SET",
           "SEEK_CUR", "SEEK_END", "DEFAULT_BUFFER_SIZE", "text_encoding",
           "IncrementalNewlineDecoder"]

# In CPython the concrete stream types inherit from the ABCs defined here. SharpPy's
# concrete types are native and cannot subclass a Python-defined ABC, so register
# them as virtual subclasses to keep isinstance(open(f), io.IOBase) and the more
# specific isinstance checks working.
for _t in (TextIOWrapper, BufferedReader, BufferedWriter, BufferedRWPair,
           BufferedRandom, FileIO, StringIO, BytesIO):
    IOBase.register(_t)
RawIOBase.register(FileIO)
for _t in (BufferedReader, BufferedWriter, BufferedRWPair, BufferedRandom, BytesIO):
    BufferedIOBase.register(_t)
for _t in (TextIOWrapper, StringIO):
    TextIOBase.register(_t)
del _t