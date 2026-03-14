"""
getattr/hasattr CPython 3.12 conformance test

Tests that getattr() and hasattr() only suppress AttributeError,
not other exception types like ModuleNotFoundError or TypeError.

CPython ref: Python/bltinmodule.c builtin_getattr, builtin_hasattr_impl
Both use _PyObject_LookupAttr() which returns:
  1  = found
  0  = AttributeError suppressed
  -1 = non-AttributeError exception, propagated
"""

passed = []
failed = []

def test(name, func):
    try:
        func()
        passed.append(name)
        print(f"  PASS  {name}")
    except AssertionError as e:
        failed.append(name)
        print(f"  FAIL  {name}: {e}")
    except Exception as e:
        failed.append(name)
        print(f"  ERROR {name}: {type(e).__name__}: {e}")

# ============================================================
# Helper classes
# ============================================================

class RaisesAttributeError:
    """__getattr__ that raises AttributeError (normal case)"""
    def __getattr__(self, name):
        raise AttributeError(f"no attr '{name}'")

class RaisesModuleNotFound:
    """__getattr__ that raises ModuleNotFoundError (bug case)"""
    def __getattr__(self, name):
        # Simulates: from nonexistent_module import something
        raise ModuleNotFoundError("No module named 'nonexistent_module'")

class RaisesTypeError:
    """__getattr__ that raises TypeError"""
    def __getattr__(self, name):
        raise TypeError(f"bad type for '{name}'")

class RaisesImportError:
    """__getattr__ that raises ImportError"""
    def __getattr__(self, name):
        raise ImportError(f"cannot import '{name}'")

class RaisesValueError:
    """__getattr__ that raises ValueError"""
    def __getattr__(self, name):
        raise ValueError(f"invalid value for '{name}'")

class NormalClass:
    """Class with real attribute"""
    x = 42

# ============================================================
# Test 1: getattr with default — AttributeError → return default
# ============================================================

def test_getattr_default_attribute_error():
    obj = RaisesAttributeError()
    result = getattr(obj, 'foo', 'DEFAULT')
    assert result == 'DEFAULT', f"Expected 'DEFAULT', got {result!r}"

test("getattr(AttributeError, default) -> default", test_getattr_default_attribute_error)

# ============================================================
# Test 2: getattr with default — ModuleNotFoundError → propagate
# ============================================================

def test_getattr_default_module_not_found():
    obj = RaisesModuleNotFound()
    try:
        result = getattr(obj, 'foo', 'DEFAULT')
        # If we reach here, the exception was suppressed (SharpPy bug)
        assert False, f"ModuleNotFoundError was suppressed, got {result!r}"
    except ModuleNotFoundError:
        pass  # Correct CPython behavior

test("getattr(ModuleNotFoundError, default) -> propagate", test_getattr_default_module_not_found)

# ============================================================
# Test 3: getattr with default — TypeError → propagate
# ============================================================

def test_getattr_default_type_error():
    obj = RaisesTypeError()
    try:
        result = getattr(obj, 'foo', 'DEFAULT')
        assert False, f"TypeError was suppressed, got {result!r}"
    except TypeError:
        pass

test("getattr(TypeError, default) -> propagate", test_getattr_default_type_error)

# ============================================================
# Test 4: getattr with default — ImportError → propagate
# ============================================================

def test_getattr_default_import_error():
    obj = RaisesImportError()
    try:
        result = getattr(obj, 'foo', 'DEFAULT')
        assert False, f"ImportError was suppressed, got {result!r}"
    except ImportError:
        pass

test("getattr(ImportError, default) -> propagate", test_getattr_default_import_error)

# ============================================================
# Test 5: getattr with default — ValueError → propagate
# ============================================================

def test_getattr_default_value_error():
    obj = RaisesValueError()
    try:
        result = getattr(obj, 'foo', 'DEFAULT')
        assert False, f"ValueError was suppressed, got {result!r}"
    except ValueError:
        pass

test("getattr(ValueError, default) -> propagate", test_getattr_default_value_error)

# ============================================================
# Test 6: getattr without default — AttributeError → propagate
# ============================================================

def test_getattr_no_default_attribute_error():
    obj = RaisesAttributeError()
    try:
        getattr(obj, 'foo')
        assert False, "Should have raised AttributeError"
    except AttributeError:
        pass

test("getattr(AttributeError, no default) -> propagate", test_getattr_no_default_attribute_error)

# ============================================================
# Test 7: getattr without default — ModuleNotFoundError → propagate
# ============================================================

def test_getattr_no_default_module_not_found():
    obj = RaisesModuleNotFound()
    try:
        getattr(obj, 'foo')
        assert False, "Should have raised"
    except ModuleNotFoundError:
        pass

test("getattr(ModuleNotFoundError, no default) -> propagate", test_getattr_no_default_module_not_found)

# ============================================================
# Test 8: hasattr — AttributeError → False
# ============================================================

def test_hasattr_attribute_error():
    obj = RaisesAttributeError()
    result = hasattr(obj, 'foo')
    assert result is False, f"Expected False, got {result!r}"

test("hasattr(AttributeError) -> False", test_hasattr_attribute_error)

# ============================================================
# Test 9: hasattr — ModuleNotFoundError → propagate
# ============================================================

def test_hasattr_module_not_found():
    obj = RaisesModuleNotFound()
    try:
        result = hasattr(obj, 'foo')
        assert False, f"ModuleNotFoundError was suppressed, got {result!r}"
    except ModuleNotFoundError:
        pass

test("hasattr(ModuleNotFoundError) -> propagate", test_hasattr_module_not_found)

# ============================================================
# Test 10: hasattr — TypeError → propagate
# ============================================================

def test_hasattr_type_error():
    obj = RaisesTypeError()
    try:
        result = hasattr(obj, 'foo')
        assert False, f"TypeError was suppressed, got {result!r}"
    except TypeError:
        pass

test("hasattr(TypeError) -> propagate", test_hasattr_type_error)

# ============================================================
# Test 11: hasattr — ImportError → propagate
# ============================================================

def test_hasattr_import_error():
    obj = RaisesImportError()
    try:
        result = hasattr(obj, 'foo')
        assert False, f"ImportError was suppressed, got {result!r}"
    except ImportError:
        pass

test("hasattr(ImportError) -> propagate", test_hasattr_import_error)

# ============================================================
# Test 12: hasattr — real attribute → True
# ============================================================

def test_hasattr_real_attribute():
    obj = NormalClass()
    assert hasattr(obj, 'x') is True

test("hasattr(real attribute) -> True", test_hasattr_real_attribute)

# ============================================================
# Test 13: getattr — real attribute → value
# ============================================================

def test_getattr_real_attribute():
    obj = NormalClass()
    assert getattr(obj, 'x') == 42
    assert getattr(obj, 'x', 99) == 42

test("getattr(real attribute) -> value", test_getattr_real_attribute)

# ============================================================
# Test 14: getattr — missing attr, no __getattr__, with default
# ============================================================

def test_getattr_no_getattr_with_default():
    obj = NormalClass()
    result = getattr(obj, 'nonexistent', 'DEFAULT')
    assert result == 'DEFAULT', f"Expected 'DEFAULT', got {result!r}"

test("getattr(no __getattr__, missing, default) -> default", test_getattr_no_getattr_with_default)

# ============================================================
# Test 15: MRO validation
# ============================================================

def test_mro_validation():
    assert not issubclass(ModuleNotFoundError, AttributeError)
    assert issubclass(ModuleNotFoundError, ImportError)
    assert not issubclass(ImportError, AttributeError)
    assert not issubclass(TypeError, AttributeError)
    assert not issubclass(ValueError, AttributeError)

test("Exception MRO validation", test_mro_validation)

# ============================================================
# Summary
# ============================================================

print()
print("=" * 50)
total = len(passed) + len(failed)
print(f"TOTAL: {len(passed)}/{total} passed, {len(failed)} failed")
if failed:
    print("FAILED:")
    for name in failed:
        print(f"  - {name}")
    print("RESULT: FAIL")
else:
    print("RESULT: ALL PASSED")
