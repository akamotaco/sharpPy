"""
Level 9: Exception Handling Test
Tests: try, except, finally, raise, multiple exceptions
"""

# Basic try-except
print("=== Basic Try-Except ===")
try:
    x = 10 / 2
    print("Result:", x)
except ZeroDivisionError:
    print("Cannot divide by zero!")

# Try-except with error
print("\n=== Try-Except with Error ===")
try:
    y = 10 / 0
    print("This won't print")
except ZeroDivisionError:
    print("Caught division by zero!")

# Multiple except blocks
print("\n=== Multiple Except ===")
try:
    value = int("not a number")
except ValueError:
    print("ValueError caught!")
except TypeError:
    print("TypeError caught!")

# Try-except-else
print("\n=== Try-Except-Else ===")
try:
    z = 10 / 2
except ZeroDivisionError:
    print("Error occurred")
else:
    print("No error, z =", z)

# Try-except-finally
print("\n=== Try-Except-Finally ===")
try:
    a = 5
    print("In try block, a =", a)
except:
    print("In except block")
finally:
    print("In finally block")

# Raising exceptions
print("\n=== Raising Exceptions ===")
try:
    raise ValueError("Custom error message")
except ValueError as e:
    print("Caught:", e)

# Nested try-except
print("\n=== Nested Try-Except ===")
try:
    try:
        b = 10 / 0
    except ZeroDivisionError:
        print("Inner except: division by zero")
        raise
except ZeroDivisionError:
    print("Outer except: re-caught")
