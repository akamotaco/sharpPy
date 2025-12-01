"""datetime module - Date and time types

CPython 3.12 compatible implementation
Uses _datetime C extension module (implemented in C# for SharpPy)
"""

try:
    from _datetime import *
    from _datetime import __doc__
except ImportError:
    # Fallback to pure Python implementation (not implemented in SharpPy yet)
    raise ImportError("_datetime module not available")

__all__ = ("date", "datetime", "time", "timedelta", "timezone", "tzinfo",
           "MINYEAR", "MAXYEAR", "UTC")
