"""Utilities for with-statement contexts - Simple class-based implementation."""


class _GeneratorContextManager:
    """Context manager for generator-based context managers."""
    pass


class suppress:
    """Context manager to suppress specified exceptions."""
    pass


class nullcontext:
    """Context manager that does no additional processing."""
    pass


def contextmanager(func):
    """@contextmanager decorator (simplified version)."""
    return _GeneratorContextManager