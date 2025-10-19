"""
Level 5: Functions - Basic Test
Tests: def, return, parameters, local variables
"""

# Simple function
def greet():
    print("Hello!")

print("=== Simple Function ===")
greet()

# Function with parameters
def add(a, b):
    return a + b

print("\n=== Function with Parameters ===")
result = add(5, 3)
print("add(5, 3) =", result)

# Function with multiple returns
def min_max(numbers):
    return min(numbers), max(numbers)

print("\n=== Multiple Return Values ===")
minimum, maximum = min_max([1, 5, 3, 9, 2])
print("min =", minimum, "max =", maximum)

# Function with local variables
def calculate():
    x = 10
    y = 20
    return x + y

print("\n=== Local Variables ===")
print("calculate() =", calculate())

# Nested function calls
def multiply(a, b):
    return a * b

def square(n):
    return multiply(n, n)

print("\n=== Nested Function Calls ===")
print("square(5) =", square(5))

# Function without return (implicit None)
def no_return():
    x = 5

print("\n=== Function Without Return ===")
result = no_return()
print("no_return() =", result)
