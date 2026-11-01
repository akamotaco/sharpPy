"""Limits test modules read at class level. On CPython the real _testcapi is found first."""
INT_MAX = 2 ** 31 - 1
INT_MIN = -2 ** 31
UINT_MAX = 2 ** 32 - 1
LONG_MAX = 2 ** 63 - 1
LONG_MIN = -2 ** 63
ULONG_MAX = 2 ** 64 - 1
PY_SSIZE_T_MAX = 2 ** 63 - 1
PY_SSIZE_T_MIN = -2 ** 63
SIZE_MAX = 2 ** 64 - 1
