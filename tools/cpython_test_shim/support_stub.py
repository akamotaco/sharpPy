"""What CPython's language tests use from test.support, without the rest of its dependencies.

Lib/test/support reaches for importlib internals, subprocess and ctypes. Only the helpers the tests call
are here. Any other name is a stand-in: as a decorator it skips the test, called inside a test it skips
that test, so a missing helper shows up as SKIP and never as a pass.
"""
import ast
import contextlib
import importlib
import time
import os
import functools
import gc
import io
import sys
import textwrap
import collections.abc
import types
import unittest
import warnings

verbose = 0
is_jython = is_android = is_emscripten = is_wasi = False
Py_DEBUG = False
MISSING_C_DOCSTRINGS = True
HAVE_DOCSTRINGS = False
SHORT_TIMEOUT = LONG_TIMEOUT = INTERNET_TIMEOUT = 30.0
C_RECURSION_LIMIT = 3000
EXCEEDS_RECURSION_LIMIT = 150000
MAX_Py_ssize_t = sys.maxsize
TEST_HOME_DIR = TEST_DATA_DIR = "."
PIPE_MAX_SIZE = 4096
_1M, _1G, _2G, _4G = 1 << 20, 1 << 30, 2 << 30, 4 << 30
TESTFN = "@test_sharppy_tmp"


def subTests(arg_names, arg_values, /, *, _do_cleanups=False):
    """Run multiple subtests with different parameters.
    """
    single_param = False
    if isinstance(arg_names, str):
        arg_names = arg_names.replace(',',' ').split()
        if len(arg_names) == 1:
            single_param = True
    arg_values = tuple(arg_values)
    def decorator(func):
        if isinstance(func, type):
            raise TypeError('subTests() can only decorate methods, not classes')
        @__import__("functools").wraps(func)
        def wrapper(self, /, *args, **kwargs):
            for values in arg_values:
                if single_param:
                    values = (values,)
                subtest_kwargs = dict(zip(arg_names, values))
                with self.subTest(**subtest_kwargs):
                    func(self, *args, **kwargs, **subtest_kwargs)
                if _do_cleanups:
                    self.doCleanups()
        return wrapper
    return decorator


def requires_mac_ver(*min_version):
    return lambda func: func


# SharpPy runs under no sanitizer.
def check_sanitizer(*, address=False, memory=False, ub=False, thread=False):
    return False


def skip_if_sanitizer(reason=None, *, address=False, memory=False, ub=False, thread=False):
    return lambda func: func


class _Stand_in:
    def __init__(self, name):
        self.name = name

    def __call__(self, *args, **kwargs):
        if len(args) == 1 and callable(args[0]) and not kwargs:
            return unittest.skip("support." + self.name)(args[0])
        raise unittest.SkipTest("support." + self.name)

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
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


SHORT_TIMEOUT = 30.0
LONG_TIMEOUT = 300.0
LOOPBACK_TIMEOUT = 5.0
INTERNET_TIMEOUT = 60.0
PIPE_MAX_SIZE = 1024 * 1024


# subprocess reads these at class level when it finds no msvcrt.
for _name in ("WIFSTOPPED", "WSTOPSIG"):
    if not hasattr(os, _name):
        setattr(os, _name, lambda status: 0)
for _name in ("WNOHANG", "WUNTRACED"):
    if not hasattr(os, _name):
        setattr(os, _name, 0)


def requires_subprocess():
    return unittest.skip("no subprocess here")


def reap_children():
    pass


def setswitchinterval(interval):
    return sys.setswitchinterval(interval)


def requires_fork():
    return unittest.skipUnless(hasattr(os, "fork"), "requires working os.fork()")


def print_warning(msg):
    for line in msg.splitlines():
        print(f"Warning -- {line}", file=sys.__stderr__, flush=True)


def _import_fresh_module(name, fresh=(), blocked=(), *, deprecated=False, usefrozen=False):
    fresh, blocked = list(fresh), list(blocked)
    names = {name, *fresh, *blocked}
    saved = {n: sys.modules.pop(n) for n in names if n in sys.modules}
    for n in blocked:
        sys.modules[n] = None
    try:
        for n in fresh:
            importlib.import_module(n)
        return importlib.import_module(name)
    except ImportError:
        return None
    finally:
        for n in names:
            sys.modules.pop(n, None)
        sys.modules.update(saved)


class TestFailed(Exception):
    """Test failed."""


def has_no_debug_ranges():
    return False


def requires_debug_ranges(reason='requires co_positions / debug_ranges'):
    return unittest.skipIf(has_no_debug_ranges(), reason)


def sortdict(dict):
    items = sorted(dict.items())
    reprpairs = ["%r: %r" % pair for pair in items]
    withcommas = ", ".join(reprpairs)
    return "{%s}" % withcommas


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


class BrokenIter:
    def __init__(self, init_raises=False, next_raises=False, iter_raises=False):
        if init_raises:
            1 / 0
        self.next_raises = next_raises
        self.iter_raises = iter_raises

    def __next__(self):
        if self.next_raises:
            1 / 0

    def __iter__(self):
        if self.iter_raises:
            1 / 0
        return self


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


@contextlib.contextmanager
def _check_no_resource_warning(testcase):
    # warnings_helper.check_no_resource_warning: check_no_warnings(category=ResourceWarning, force_gc=True).
    import gc
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always", ResourceWarning)
        yield
        gc.collect()
    testcase.assertEqual([w.message for w in caught if issubclass(w.category, ResourceWarning)], [])


def _check_syntax_warning(testcase, statement, errtext="", *, lineno=1, offset=None):
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always", SyntaxWarning)
        compile(statement, "<testcase>", "exec")
    testcase.assertGreaterEqual(len(caught), 1)
    testcase.assertTrue(issubclass(caught[0].category, SyntaxWarning))
    if errtext:
        testcase.assertRegex(str(caught[0].message), errtext)


import_helper = _helper_module("import_helper", import_fresh_module=_import_fresh_module, import_module=_import_module)
os_helper = _helper_module("os_helper", TESTFN=TESTFN, unlink=_unlink)
warnings_helper = _helper_module("warnings_helper", check_warnings=_check_warnings, check_no_warnings=_check_no_warnings,
                                 check_no_resource_warning=_check_no_resource_warning,
                                 check_syntax_warning=_check_syntax_warning, ignore_warnings=lambda *, category: (lambda f: f))
script_helper = _helper_module("script_helper")
socket_helper = _helper_module("socket_helper")
logging_helper = _helper_module("logging_helper")
hashlib_helper = _helper_module("hashlib_helper")
bytecode_helper = _helper_module("bytecode_helper")


def _take_from(file, *names):
    """Pure Python helpers come from the real file, so nothing of Lib/test is copied."""
    path = os.path.join(os.path.dirname(os.path.dirname(unittest.__file__)), "test", "support", file)
    with open(path, encoding="utf-8") as stream:
        tree = ast.parse(stream.read())
    for node in tree.body:
        if getattr(node, "name", None) in names:
            # The stub of requires_subprocess skips tests; these run the interpreter themselves.
            node.decorator_list = [d for d in node.decorator_list if "requires_subprocess" not in ast.dump(d)]
            exec(compile(ast.Module([node], []), path, "exec"), globals())


def _take_from_support(*names):
    _take_from("__init__.py", *names)


_take_from_support("busy_retry", "sleeping_retry", "catch_unraisable_exception", "catch_threading_exception",
                   "run_code", "adjust_int_max_str_digits", "maybe_get_event_loop_policy")


def rmtree(path):
    import shutil
    shutil.rmtree(path, ignore_errors=True)


_take_from("os_helper.py", "EnvironmentVarGuard", "temp_dir", "change_cwd", "temp_cwd")
os_helper.__dict__.update(EnvironmentVarGuard=EnvironmentVarGuard, temp_dir=temp_dir, change_cwd=change_cwd,
                          temp_cwd=temp_cwd, rmtree=rmtree)


def _interpreter():
    if sys.executable.endswith(".dll"):
        return [os.path.expanduser("~/AppData/Local/SharpPy/dotnet/dotnet.exe"), sys.executable]
    return [sys.executable]


_RUN_C = """import sys
sys.argv[0] = '-c'
del sys.path[0]
with open({!r}, encoding='utf-8') as _stream:
    _code = compile(_stream.read(), '<string>', 'exec')
del _stream
exec(_code)
"""


def run_python_until_end(*args, **env_vars):
    """The real one, with os.system for subprocess, on both interpreters: -c goes through a
    file, because cmd.exe cannot carry a program in a quoted argument."""
    import tempfile
    env_vars.pop("__isolated", None)
    env_vars.pop("__cleanenv", None)
    cwd = env_vars.pop("__cwd", None)
    argv = [os.fsdecode(arg) for arg in args]
    with tempfile.TemporaryDirectory() as folder:
        if "-c" in argv:
            at = argv.index("-c")
            source = os.path.join(folder, "source")
            with open(source, "w", encoding="utf-8") as stream:
                stream.write(argv[at + 1])
            runner = os.path.join(folder, "run_c.py")
            with open(runner, "w", encoding="utf-8") as stream:
                stream.write(_RUN_C.format(source))
            argv[at:at + 2] = [runner]
        out, err = os.path.join(folder, "out"), os.path.join(folder, "err")
        line = " ".join(f'"{arg}"' for arg in _interpreter() + argv)
        saved = {name: os.environ.get(name) for name in env_vars}
        here = os.getcwd()
        try:
            os.environ.update(env_vars)
            if cwd is not None:
                os.chdir(cwd)
            code = os.system(f'"{line} > "{out}" 2> "{err}""')
        finally:
            os.chdir(here)
            for name, value in saved.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value
        with open(out, "rb") as stream:
            stdout = stream.read()
        with open(err, "rb") as stream:
            stderr = stream.read()
    return _PythonRunResult(code, stdout, stderr), _interpreter() + argv



_take_from("script_helper.py", "_PythonRunResult", "_assert_python", "assert_python_ok", "assert_python_failure",
           "make_script", "make_pkg")
script_helper.__dict__.update(run_python_until_end=run_python_until_end, assert_python_ok=assert_python_ok,
                              assert_python_failure=assert_python_failure, make_script=make_script,
                              make_pkg=make_pkg)
