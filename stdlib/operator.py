"""
Minimal operator module for SharpPy
Wraps _operator C# module
"""

# Import all functions from _operator backend
from _operator import *

# For compatibility - ensure index is available
from _operator import index
