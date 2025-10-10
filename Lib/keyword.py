"""Keywords (from "Grammar/python.gram")

CPython 3.12 compatible keyword list for SharpPy
"""

# CPython 3.12 keywords
kwlist = [
    'False',
    'None',
    'True',
    'and',
    'as',
    'assert',
    'async',
    'await',
    'break',
    'class',
    'continue',
    'def',
    'del',
    'elif',
    'else',
    'except',
    'finally',
    'for',
    'from',
    'global',
    'if',
    'import',
    'in',
    'is',
    'lambda',
    'nonlocal',
    'not',
    'or',
    'pass',
    'raise',
    'return',
    'try',
    'while',
    'with',
    'yield',
]

def iskeyword(s):
    """Return True if s is a Python keyword."""
    return s in kwlist

def issoftkeyword(s):
    """Return True if s is a soft keyword (match, case, type, _)."""
    return s in ('match', 'case', 'type', '_')

__all__ = ['iskeyword', 'issoftkeyword', 'kwlist']
