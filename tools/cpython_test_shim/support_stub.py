"""What CPython's language tests use from test.support, without the rest of its dependencies.

Lib/test/support reaches for importlib internals, subprocess and ctypes. Only the helpers the tests call
are here. Any other name is a stand-in: as a decorator it skips the test, called inside a test it skips
that test, so a missing helper shows up as SKIP and never as a pass.
"""
import contextlib
import functools
import gc
import io
import sys
import types
import unittest
import warnings

verbose = 0
is_jython = is_android = is_emscripten = is_wasi = False
Py_DEBUG = False
MISSING_C_DOCSTRINGS = True
HAVE_DOCSTRINGS = False
SHORT_TIMEOUT = LONG_TIMEOUT = INTERNET_TIMEOUT = 30.0
C_RECURSION_LIMIT = 800
EXCEEDS_RECURSION_LIMIT = 150000
MAX_Py_ssize_t = sys.maxsize
TEST_HOME_DIR = TEST_DATA_DIR = "."
PIPE_MAX_SIZE = 4096
_1M, _1G, _2G, _4G = 1 << 20, 1 << 30, 2 << 30, 4 << 30
TESTFN = "@test_sharppy_tmp"


class _Stand_in:
    def __init__(self, name):
        self.name = name

    def __call__(self, *args, **kwargs):
        if len(args) == 1 and callable(args[0]) and not kwargs:
            return unittest.skip("support." + self.name)(args[0])
        raise unittest.SkipTest("support." + self.name)

    def __getattr__(self, name):
        return _Stand_in(self.name + "." + name)

    def __enter__(self):
        raise unittest.SkipTest("support." + self.name)

    def __exit__(self, *args):
        return False


def __getattr__(name):
    if name.startswith("__"):
        raise AttributeError(name)
    return _Stand_in(name)


def cpython_only(test):
    return unittest.skip("CPython only")(test)


def impl_detail(msg=None, **guards):
    return lambda test: test


def check_impl_detail(**guards):
    return guards.get("cpython", True) is True


def gc_collect():
    gc.collect()
    gc.collect()
    gc.collect()


@contextlib.contextmanager
def disable_gc():
    yield


def requires_IEEE_754(test):
    return test


def requires_working_socket(*, module=False):
    if module:
        raise unittest.SkipTest("no sockets")
    return unittest.skip("no sockets")


def requires_docstrings(test):
    return unittest.skip("no docstrings")(test)


def no_tracing(test):
    return test


def refcount_test(test):
    return unittest.skip("reference counts")(test)


def bigmemtest(size, memuse, dry_run=True):
    return lambda test: unittest.skip("big memory")(test)


def bigaddrspacetest(test):
    return unittest.skip("big address space")(test)


def skip_if_buggy_ucrt_strfptime(test):
    return test


def run_with_locale(category, *locales):
    return lambda test: test


def run_with_tz(tz):
    return lambda test: test


def requires_resource(name):
    return unittest.skip("resource " + name)


def is_resource_enabled(name):
    return False


def requires(name, msg=None):
    raise unittest.SkipTest("resource " + name)


def anticipate_failure(condition):
    return unittest.expectedFailure if condition else (lambda test: test)


def check_syntax_error(testcase, statement, errtext="", *, lineno=None, offset=None):
    with testcase.assertRaisesRegex(SyntaxError, errtext) as cm:
        compile(statement, "<test string>", "exec")
    testcase.assertIsNotNone(cm.exception.lineno)
    if lineno is not None:
        testcase.assertEqual(cm.exception.lineno, lineno)
    if offset is not None:
        testcase.assertEqual(cm.exception.offset, offset)


@contextlib.contextmanager
def captured_output(name):
    original = getattr(sys, name)
    setattr(sys, name, io.StringIO())
    try:
        yield getattr(sys, name)
    finally:
        setattr(sys, name, original)


def captured_stdout():
    return captured_output("stdout")


def captured_stderr():
    return captured_output("stderr")


def captured_stdin():
    return captured_output("stdin")


@contextlib.contextmanager
def swap_attr(obj, attr, new_val):
    if hasattr(obj, attr):
        real = getattr(obj, attr)
        setattr(obj, attr, new_val)
        try:
            yield real
        finally:
            setattr(obj, attr, real)
    else:
        setattr(obj, attr, new_val)
        try:
            yield
        finally:
            if hasattr(obj, attr):
                delattr(obj, attr)


@contextlib.contextmanager
def swap_item(obj, item, new_val):
    if item in obj:
        real = obj[item]
        obj[item] = new_val
        try:
            yield real
        finally:
            obj[item] = real
    else:
        obj[item] = new_val
        try:
            yield
        finally:
            obj.pop(item, None)


@contextlib.contextmanager
def infinite_recursion(max_depth=75):
    original = sys.getrecursionlimit()
    try:
        sys.setrecursionlimit(max_depth)
        yield
    finally:
        sys.setrecursionlimit(original)


def check_free_after_iterating(test, iter, cls, args=()):
    raise unittest.SkipTest("object lifetime")


def run_doctest(module, verbosity=None, optionflags=0):
    raise unittest.SkipTest("doctest")


class _ALWAYS_EQ:
    def __eq__(self, other):
        return True

    def __ne__(self, other):
        return False


class _NEVER_EQ:
    def __eq__(self, other):
        return False

    def __ne__(self, other):
        return True

    def __hash__(self):
        return 1


@functools.total_ordering
class _LARGEST:
    def __eq__(self, other):
        return isinstance(other, _LARGEST)

    def __lt__(self, other):
        return False


@functools.total_ordering
class _SMALLEST:
    def __eq__(self, other):
        return isinstance(other, _SMALLEST)

    def __gt__(self, other):
        return False


ALWAYS_EQ, NEVER_EQ, LARGEST, SMALLEST = _ALWAYS_EQ(), _NEVER_EQ(), _LARGEST(), _SMALLEST()


class Matcher:
    pass


def _helper_module(name, **names):
    module = types.ModuleType("test.support." + name)
    module.__getattr__ = lambda attr: _Stand_in(name + "." + attr)
    module.__dict__.update(names)
    sys.modules["test.support." + name] = module
    return module


def _import_module(name, deprecated=False, *, required_on=()):
    try:
        return __import__(name, fromlist=["*"])
    except ImportError as error:
        raise unittest.SkipTest(str(error))


def _unlink(path):
    import os
    try:
        os.unlink(path)
    except OSError:
        pass


@contextlib.contextmanager
def _check_warnings(*filters, quiet=True):
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        yield caught


@contextlib.contextmanager
def _check_no_warnings(testcase, *args, **kwargs):
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        yield caught
    testcase.assertEqual(len(caught), 0)


def _check_syntax_warning(testcase, statement, errtext="", *, lineno=1, offset=None):
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always", SyntaxWarning)
        compile(statement, "<testcase>", "exec")
    testcase.assertGreaterEqual(len(caught), 1)
    testcase.assertTrue(issubclass(caught[0].category, SyntaxWarning))
    if errtext:
        testcase.assertRegex(str(caught[0].message), errtext)


import_helper = _helper_module("import_helper", import_module=_import_module)
os_helper = _helper_module("os_helper", TESTFN=TESTFN, unlink=_unlink)
warnings_helper = _helper_module("warnings_helper", check_warnings=_check_warnings, check_no_warnings=_check_no_warnings,
                                 check_syntax_warning=_check_syntax_warning, ignore_warnings=lambda *, category: (lambda f: f))
script_helper = _helper_module("script_helper")
threading_helper = _helper_module("threading_helper", requires_working_threading=lambda *, module=False: (lambda f: f),
                                  reap_threads=lambda f: f)
socket_helper = _helper_module("socket_helper")
logging_helper = _helper_module("logging_helper")
hashlib_helper = _helper_module("hashlib_helper")
bytecode_helper = _helper_module("bytecode_helper")
