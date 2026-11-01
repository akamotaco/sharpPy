"""Run one module of CPython's Lib/test and write one line per test as it finishes.

usage: cpython_test_driver.py <test module> <output file>
Runs under CPython and under SharpPy alike; run_cpython_tests.py compares the two.
"""
import os
import sys

CPYTHON_LIB = os.environ["CPYTHON_LIB"]
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])), "cpython_test_shim"))
sys.path.append(CPYTHON_LIB)

name, target = sys.argv[1], sys.argv[2]
output = open(target, "w", encoding="utf-8")


def emit(state, test, detail=""):
    text = str(detail).replace("\n", " | ")[-300:]
    output.write(f"{state}\t{test}\t{text}\n")
    output.flush()


def fill_importlib():
    """The part of importlib the test machinery reads; SharpPy's importlib is a small native module."""
    import importlib
    import inspect
    import types
    machinery = types.ModuleType("importlib.machinery")
    machinery.SOURCE_SUFFIXES = [".py"]
    machinery.DEBUG_BYTECODE_SUFFIXES = machinery.BYTECODE_SUFFIXES = [".pyc"]
    machinery.OPTIMIZED_BYTECODE_SUFFIXES = [".pyc"]
    machinery.EXTENSION_SUFFIXES = []
    machinery.all_suffixes = lambda: [".py", ".pyc"]
    util = types.ModuleType("importlib.util")
    sys.modules["importlib.machinery"] = importlib.machinery = machinery
    sys.modules["importlib.util"] = importlib.util = util
    inspect.importlib = importlib


try:
    try:
        import importlib.machinery
    except ImportError:
        fill_importlib()
    import test
    test.__path__.insert(0, os.path.join(CPYTHON_LIB, "test"))
    import unittest
    # The same small test.support on both sides, so both run the same tests.
    import support_stub
    support_stub.__name__ = "test.support"
    support_stub.__path__ = [os.path.join(CPYTHON_LIB, "test", "support")]
    sys.modules["test.support"] = test.support = support_stub
    module = __import__("test." + name, fromlist=["*"])
    suite = unittest.defaultTestLoader.loadTestsFromModule(module)
except BaseException as error:
    emit("IMPORT", name, f"{type(error).__name__}: {error}")
    sys.exit(0)


class Result(unittest.TestResult):
    def __init__(self):
        super().__init__()
        self.current = None

    def startTest(self, test):
        super().startTest(test)
        emit("START", test.id())

    def addSuccess(self, test):
        emit("PASS", test.id())

    def addError(self, test, err):
        emit("FAIL", test.id(), f"{err[0].__name__}: {err[1]}")

    def addFailure(self, test, err):
        emit("FAIL", test.id(), f"{err[0].__name__}: {err[1]}")

    def addSkip(self, test, reason):
        emit("SKIP", test.id(), reason)

    def addExpectedFailure(self, test, err):
        emit("PASS", test.id())

    def addUnexpectedSuccess(self, test):
        emit("FAIL", test.id(), "unexpected success")

    def addSubTest(self, test, subtest, err):
        if err is not None:
            emit("FAIL", test.id(), f"subtest {subtest}: {err[0].__name__}: {err[1]}")


suite.run(Result())
emit("END", name)
