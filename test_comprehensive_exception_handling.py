print("=== Comprehensive Exception Handling Test ===")

# Test 1: Basic try-except-else-finally
print("\n1. Basic try-except-else-finally (no exception):")
try:
    print("  In try block")
    x = 10 / 2
except ZeroDivisionError:
    print("  In except block")
else:
    print("  In else block")
finally:
    print("  In finally block")

# Test 2: try-except-else-finally (with exception)
print("\n2. try-except-else-finally (with exception):")
try:
    print("  In try block")
    x = 10 / 0
except ZeroDivisionError:
    print("  In except block")
else:
    print("  In else block")
finally:
    print("  In finally block")

# Test 3: Multiple except blocks
print("\n3. Multiple except blocks:")
try:
    print("  In try block")
    raise ValueError("test error")
except ZeroDivisionError:
    print("  In ZeroDivisionError except")
except ValueError as e:
    print(f"  In ValueError except: {e}")
except Exception as e:
    print(f"  In general Exception except: {e}")
else:
    print("  In else block")
finally:
    print("  In finally block")

# Test 4: Nested try-except
print("\n4. Nested try-except:")
try:
    print("  Outer try")
    try:
        print("    Inner try")
        raise RuntimeError("inner error")
    except RuntimeError as e:
        print(f"    Inner except: {e}")
    print("  After inner try-except")
except Exception as e:
    print(f"  Outer except: {e}")
else:
    print("  Outer else")
finally:
    print("  Outer finally")

# Test 5: try-finally only
print("\n5. try-finally only:")
try:
    print("  In try block")
    result = 42
finally:
    print("  In finally block")

# Test 6: try-except only (no else/finally)
print("\n6. try-except only:")
try:
    print("  In try block")
    raise TypeError("test type error")
except TypeError as e:
    print(f"  In except block: {e}")

print("\n=== All tests completed ===")