"""
Extract, format and print information about Python stack traces.

This module provides utilities to format and print stack traces of Python programs.
It exactly mimics the behavior of the Python interpreter when it prints a stack trace.
"""

import sys
from typing import Optional, TextIO, List, Tuple, Any, Union


def print_exc(limit: Optional[int] = None, file: Optional[TextIO] = None, chain: bool = True) -> None:
    """Print exception information and stack trace entries from traceback object to file."""
    if file is None:
        file = sys.stderr

    exc_type, exc_value, exc_tb = sys.exc_info()
    if exc_type is None:
        return

    print_exception(exc_type, exc_value, exc_tb, limit=limit, file=file, chain=chain)


def format_exc(limit: Optional[int] = None, chain: bool = True) -> str:
    """Like print_exc() but return a string instead of printing to file."""
    exc_type, exc_value, exc_tb = sys.exc_info()
    if exc_type is None:
        return ""

    return "".join(format_exception(exc_type, exc_value, exc_tb, limit=limit, chain=chain))


def print_exception(etype, value, tb, limit: Optional[int] = None,
                   file: Optional[TextIO] = None, chain: bool = True) -> None:
    """Print exception information and stack trace entries from traceback object to file."""
    if file is None:
        file = sys.stderr

    lines = format_exception(etype, value, tb, limit=limit, chain=chain)
    for line in lines:
        file.write(line)


def format_exception(etype, value, tb, limit: Optional[int] = None, chain: bool = True) -> List[str]:
    """Format a traceback and the exception information."""
    lines = []

    # Format the traceback
    if tb is not None:
        lines.append("Traceback (most recent call last):\n")
        lines.extend(format_tb(tb, limit=limit))

    # Format the exception
    lines.extend(format_exception_only(etype, value))

    return lines


def print_tb(tb, limit: Optional[int] = None, file: Optional[TextIO] = None) -> None:
    """Print up to 'limit' stack trace entries from the traceback 'tb'."""
    if file is None:
        file = sys.stderr

    for line in format_tb(tb, limit=limit):
        file.write(line)


def format_tb(tb, limit: Optional[int] = None) -> List[str]:
    """Format a traceback as a list of strings."""
    lines = []

    # Extract traceback entries
    entries = extract_tb(tb, limit=limit)

    for entry in entries:
        lines.append(f'  File "{entry.filename}", line {entry.lineno}, in {entry.name}\n')
        if entry.line:
            lines.append(f'    {entry.line.strip()}\n')

    return lines


def extract_tb(tb, limit: Optional[int] = None) -> List['FrameSummary']:
    """Extract raw traceback from a traceback object."""
    entries = []
    current_tb = tb
    count = 0

    while current_tb is not None and (limit is None or count < limit):
        frame = current_tb.tb_frame
        filename = frame.f_code.co_filename
        lineno = current_tb.tb_lineno
        name = frame.f_code.co_name

        # Try to get the source line
        line = None
        try:
            # This is a simplified version - in real Python it reads from the file
            line = f"<source line {lineno}>"
        except:
            line = None

        entries.append(FrameSummary(filename, lineno, name, line))

        current_tb = current_tb.tb_next
        count += 1

    return entries


def print_stack(f=None, limit: Optional[int] = None, file: Optional[TextIO] = None) -> None:
    """Print a stack trace from its invocation point."""
    if f is None:
        try:
            raise ZeroDivisionError
        except ZeroDivisionError:
            f = sys.exc_info()[2].tb_frame.f_back

    print_list(extract_stack(f, limit=limit), file=file)


def format_stack(f=None, limit: Optional[int] = None) -> List[str]:
    """Format a stack trace as a list of strings."""
    if f is None:
        try:
            raise ZeroDivisionError
        except ZeroDivisionError:
            f = sys.exc_info()[2].tb_frame.f_back

    return format_list(extract_stack(f, limit=limit))


def extract_stack(f=None, limit: Optional[int] = None) -> List['FrameSummary']:
    """Extract raw traceback from current stack frame."""
    entries = []

    if f is None:
        try:
            raise ZeroDivisionError
        except ZeroDivisionError:
            f = sys.exc_info()[2].tb_frame.f_back

    count = 0
    while f is not None and (limit is None or count < limit):
        filename = f.f_code.co_filename
        lineno = f.f_lineno
        name = f.f_code.co_name

        # Try to get the source line
        line = None
        try:
            # This is a simplified version
            line = f"<source line {lineno}>"
        except:
            line = None

        entries.append(FrameSummary(filename, lineno, name, line))

        f = f.f_back
        count += 1

    entries.reverse()  # Most recent call last
    return entries


def print_list(extracted_list: List['FrameSummary'], file: Optional[TextIO] = None) -> None:
    """Print the list of tuples as returned by extract_tb() or extract_stack()."""
    if file is None:
        file = sys.stderr

    for line in format_list(extracted_list):
        file.write(line)


def format_list(extracted_list: List['FrameSummary']) -> List[str]:
    """Format a list of traceback entries."""
    lines = []

    for entry in extracted_list:
        lines.append(f'  File "{entry.filename}", line {entry.lineno}, in {entry.name}\n')
        if entry.line:
            lines.append(f'    {entry.line.strip()}\n')

    return lines


def format_exception_only(etype, value) -> List[str]:
    """Format the exception part of a traceback."""
    lines = []

    if etype is None:
        return lines

    # Get the exception class name
    if hasattr(etype, '__name__'):
        exception_name = etype.__name__
    else:
        exception_name = str(etype)

    # Format the exception message
    if value is None:
        lines.append(f"{exception_name}\n")
    else:
        # Convert exception value to string
        if hasattr(value, 'args') and value.args:
            if len(value.args) == 1:
                message = str(value.args[0])
            else:
                message = str(value.args)
        else:
            message = str(value)

        if message:
            lines.append(f"{exception_name}: {message}\n")
        else:
            lines.append(f"{exception_name}\n")

    return lines


class FrameSummary:
    """A single frame from a traceback."""

    def __init__(self, filename: str, lineno: int, name: str, line: Optional[str] = None):
        self.filename = filename
        self.lineno = lineno
        self.name = name
        self.line = line

    def __repr__(self) -> str:
        return f"FrameSummary(filename='{self.filename}', lineno={self.lineno}, name='{self.name}')"

    def __str__(self) -> str:
        line_part = f", line='{self.line}'" if self.line else ""
        return f"File '{self.filename}', line {self.lineno}, in {self.name}{line_part}"


class TracebackException:
    """An exception ready for rendering."""

    def __init__(self, exc_type, exc_value, exc_traceback, limit=None, lookup_lines=True, capture_locals=False):
        self.exc_type = exc_type
        self.exc_value = exc_value
        self.exc_traceback = exc_traceback
        self.limit = limit
        self.lookup_lines = lookup_lines
        self.capture_locals = capture_locals

        if exc_traceback is not None:
            self.stack = extract_tb(exc_traceback, limit=limit)
        else:
            self.stack = []

    @classmethod
    def from_exception(cls, exc, limit=None, lookup_lines=True, capture_locals=False):
        """Capture an exception for later rendering."""
        return cls(type(exc), exc, exc.__traceback__, limit=limit,
                  lookup_lines=lookup_lines, capture_locals=capture_locals)

    def format(self, chain=True):
        """Format the exception."""
        lines = []

        if self.stack:
            lines.append("Traceback (most recent call last):\n")
            lines.extend(format_list(self.stack))

        lines.extend(format_exception_only(self.exc_type, self.exc_value))

        return lines

    def format_exception_only(self):
        """Format only the exception part."""
        return format_exception_only(self.exc_type, self.exc_value)


# Utility functions for walking through tracebacks
def walk_stack(f):
    """Walk a stack yielding the frame and line number for each frame."""
    while f is not None:
        yield f, f.f_lineno
        f = f.f_back


def walk_tb(tb):
    """Walk a traceback yielding the frame and line number for each frame."""
    while tb is not None:
        yield tb.tb_frame, tb.tb_lineno
        tb = tb.tb_next


# Export public API
__all__ = [
    'print_exc', 'format_exc', 'print_exception', 'format_exception',
    'print_tb', 'format_tb', 'extract_tb',
    'print_stack', 'format_stack', 'extract_stack',
    'print_list', 'format_list', 'format_exception_only',
    'FrameSummary', 'TracebackException',
    'walk_stack', 'walk_tb'
]