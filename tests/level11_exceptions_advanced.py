"""
Level 11: Advanced Exception Handling (Python 3.11+)
Tests for exception chaining and exception groups
"""

# Test 1: Simple exception chaining
print("=== Test 1: Simple Exception Chaining ===")
try:
    raise ValueError("original") from TypeError("cause")
except ValueError as e:
    print(f"Caught: {e}")
    print(f"__cause__: {e.__cause__}")

# Test 2: Nested exception chaining with __cause__ access
print("\n=== Test 2: Nested Exception Chaining ===")
try:
    try:
        raise ValueError("inner")
    except ValueError as e:
        raise TypeError("outer") from e
except TypeError as e2:
    print(f"Caught: {e2}")
    print(f"__cause__: {e2.__cause__}")

# Test 3: Type checking on __cause__
print("\n=== Test 3: __cause__ Type Check ===")
try:
    try:
        raise ValueError("inner")
    except ValueError as e:
        raise TypeError("outer") from e
except TypeError as e2:
    cause = e2.__cause__
    print(f"Caught type: {type(e2).__name__}")
    print(f"Cause type: {type(cause).__name__}")
    print(f"Cause value: {cause}")

# Test 4: Exception group with except*
print("\n=== Test 4: Exception Group ===")
try:
    raise ExceptionGroup("group", [ValueError("v")])
except* ValueError as e:
    print("Caught ValueError in exception group")

print("\n=== All tests passed! ===")
