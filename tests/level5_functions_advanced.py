"""
Level 5: Functions - Advanced Test
Tests: default parameters, *args, **kwargs, keyword arguments
"""

# Default parameters
def greet(name="World"):
    print(f"Hello, {name}!")

print("=== Default Parameters ===")
greet()
greet("Alice")

# Multiple default parameters
def describe(name, age=0, city="Unknown"):
    print(f"{name}, age {age}, from {city}")

print("\n=== Multiple Defaults ===")
describe("Bob")
describe("Charlie", 25)
describe("Diana", 30, "NYC")

# Keyword arguments
print("\n=== Keyword Arguments ===")
describe(city="LA", name="Eve", age=28)

# *args
def sum_all(*args):
    total = 0
    for num in args:
        total += num
    return total

print("\n=== *args ===")
print("sum_all(1, 2, 3) =", sum_all(1, 2, 3))
print("sum_all(1, 2, 3, 4, 5) =", sum_all(1, 2, 3, 4, 5))

# **kwargs
def print_info(**kwargs):
    for key, value in kwargs.items():
        print(f"{key}: {value}")

print("\n=== **kwargs ===")
print_info(name="Frank", age=35, job="Engineer")

# Mix: positional, *args, **kwargs
def complex_func(a, b, *args, **kwargs):
    print("a =", a)
    print("b =", b)
    print("args =", args)
    print("kwargs =", kwargs)

print("\n=== Complex Function ===")
complex_func(1, 2, 3, 4, x=10, y=20)
