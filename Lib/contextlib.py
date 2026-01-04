"""contextlib - Context manager utilities (CPython 3.12 compatible)"""

import functools
import sys


class AbstractContextManager:
    """An abstract base class for context managers."""

    def __enter__(self):
        """Return `self` upon entering the runtime context."""
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        """Raise any exception triggered within the runtime context."""
        return None


class AbstractAsyncContextManager:
    """An abstract base class for async context managers."""

    async def __aenter__(self):
        """Return `self` upon entering the runtime context."""
        return self

    async def __aexit__(self, exc_type, exc_value, traceback):
        """Raise any exception triggered within the runtime context."""
        return None


class _GeneratorContextManager(AbstractContextManager):
    """Helper for @contextmanager decorator.

    CPython 3.12: Lib/contextlib.py lines 125-196

    SharpPy workaround: Delay generator creation until __enter__
    to avoid *args unpacking bug in __init__
    """

    def __init__(self, func, args, kwds):
        # SharpPy workaround: Don't call generator yet
        # Store func and args for later
        self.func = func
        self.args = args
        self.kwds = kwds
        self.gen = None
        # docstring preservation
        doc = getattr(func, "__doc__", None)
        if doc is None:
            doc = type(self).__doc__
        self.__doc__ = doc

    def __enter__(self):
        # SharpPy workaround: Create generator here
        # This avoids *args unpacking in __init__
        if self.args:
            # Manual unpacking to avoid *args bug
            arg_count = len(self.args)
            if arg_count == 0:
                self.gen = self.func()
            elif arg_count == 1:
                self.gen = self.func(self.args[0])
            elif arg_count == 2:
                self.gen = self.func(self.args[0], self.args[1])
            elif arg_count == 3:
                self.gen = self.func(self.args[0], self.args[1], self.args[2])
            else:
                # Fallback: try *args unpacking (may fail)
                self.gen = self.func(*self.args)
        else:
            self.gen = self.func()

        # Memory optimization
        self.args = None
        self.kwds = None
        self.func = None

        try:
            return next(self.gen)
        except StopIteration:
            raise RuntimeError("generator didn't yield") from None

    def __exit__(self, type, value, traceback):
        if type is None:
            # No exception - normal exit
            # CPython 3.12: contextlib.py lines 141-151
            try:
                next(self.gen)
            except StopIteration:
                return False
            else:
                raise RuntimeError("generator didn't stop")
        else:
            # Exception occurred
            # CPython 3.12: contextlib.py lines 152-196
            if value is None:
                # Need to force instantiation so we can reliably
                # tell if we get the same exception back
                value = type()
            try:
                self.gen.throw(type, value, traceback)
                raise RuntimeError("generator didn't stop after throw()")
            except StopIteration as exc:
                # Suppress StopIteration *unless* it's the same exception that
                # was passed to throw(). This prevents a StopIteration
                # raised inside the "with" statement from being suppressed.
                return exc is not value
            except RuntimeError as exc:
                # Don't re-raise the passed in exception. (issue27122)
                # PEP 479: If StopIteration was wrapped in RuntimeError,
                # check if it's the same as the thrown exception
                if exc is value:
                    return False
                # Detect PEP 479 case: StopIteration wrapped in RuntimeError
                # Check __cause__ for chained exceptions
                _exc_details = sys.exc_info()
                if isinstance(value, StopIteration):
                    # Check if this RuntimeError was caused by the StopIteration we threw
                    if hasattr(exc, '__cause__') and exc.__cause__ is value:
                        return False
                raise
            except:
                # only re-raise if it's *not* the exception that was
                # passed to throw(), because __exit__() must not raise
                # an exception unless __exit__() itself failed. But throw()
                # has to raise the exception to signal propagation, so this
                # fixes the impedance mismatch between the throw() protocol
                # and the __exit__() protocol.
                _exc_details = sys.exc_info()
                if _exc_details[1] is value:
                    return False
                raise


def contextmanager(func):
    """@contextmanager decorator.

    Typical usage:

        @contextmanager
        def some_generator(<arguments>):
            <setup>
            try:
                yield <value>
            finally:
                <cleanup>

    This makes this:

        with some_generator(<arguments>) as <variable>:
            <body>

    equivalent to this:

        <setup>
        try:
            <variable> = <value>
            <body>
        finally:
            <cleanup>

    CPython 3.12: Lib/contextlib.py lines 272-302
    """
    @functools.wraps(func)
    def helper(*args, **kwds):
        return _GeneratorContextManager(func, args, kwds)
    return helper


class suppress(AbstractContextManager):
    """Context manager to suppress specified exceptions

    After the exception is suppressed, execution proceeds with the next
    statement following the with statement.

         with suppress(FileNotFoundError):
             os.remove(somefile)
         # Execution still resumes here if the file was already removed
    """

    def __init__(self, *exceptions):
        self._exceptions = exceptions

    def __enter__(self):
        pass

    def __exit__(self, exctype, excinst, exctb):
        # Unlike isinstance and issubclass, CPython exception handling
        # currently only looks at the concrete type hierarchy (ignoring
        # the instance and subclass checking hooks). While Guido considers
        # that a bug rather than a feature, it's a fairly hard one to fix
        # due to various internal implementation details. suppress provides
        # the simpler issubclass based semantics, rather than trying to
        # exactly reproduce the limitations of the CPython interpreter.
        #
        # See http://bugs.python.org/issue12029 for more details
        if exctype is None:
            return
        if issubclass(exctype, self._exceptions):
            return True
        # Note: BaseExceptionGroup handling removed for simplicity
        # Original CPython code checks for BaseExceptionGroup.split()
        return False
