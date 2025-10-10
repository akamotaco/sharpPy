# Minimal traceback module for SharpPy
# CPython 3.12 compatible implementation using sys.exc_info()

import sys

def print_exc(limit=None, file=None, chain=True):
    """Print exception information from sys.exc_info()"""
    exc_info = sys.exc_info()
    exc_type = exc_info[0]
    exc_value = exc_info[1]
    exc_tb = exc_info[2]

    if exc_type is None:
        return

    # Simple formatting: print exception type and value
    if file is None:
        print(f"Traceback (most recent call last):")
        if exc_value is not None:
            print(f"{exc_type.__name__}: {exc_value}")
        else:
            print(f"{exc_type.__name__}")
    else:
        file.write(f"Traceback (most recent call last):\n")
        if exc_value is not None:
            file.write(f"{exc_type.__name__}: {exc_value}\n")
        else:
            file.write(f"{exc_type.__name__}\n")

def format_exc(limit=None, chain=True):
    """Format exception as string"""
    exc_info = sys.exc_info()
    exc_type = exc_info[0]
    exc_value = exc_info[1]
    exc_tb = exc_info[2]

    if exc_type is None:
        return "NoneType: None\n"

    # Simple formatting: return exception type and value as string
    if exc_value is not None:
        return f"Traceback (most recent call last):\n{exc_type.__name__}: {exc_value}\n"
    else:
        return f"Traceback (most recent call last):\n{exc_type.__name__}\n"

__all__ = ['print_exc', 'format_exc']

