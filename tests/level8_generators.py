"""
Level 8: Generators Test
Tests: yield, generator functions, generator expressions
"""

# Simple generator
def count_up_to(n):
    i = 0
    while i < n:
        yield i
        i += 1

print("=== Simple Generator ===")
for num in count_up_to(5):
    print(num)

# Generator with values
def fibonacci(n):
    a, b = 0, 1
    for _ in range(n):
        yield a
        a, b = b, a + b

print("\n=== Fibonacci Generator ===")
for fib in fibonacci(7):
    print(fib, end=" ")
print()

# Generator expression
print("\n=== Generator Expression ===")
gen = (x**2 for x in range(5))
for val in gen:
    print(val, end=" ")
print()

# Generator with send (if supported)
def echo():
    while True:
        value = yield
        if value is None:
            break
        yield value * 2

print("\n=== Generator Protocol ===")
gen = echo()
next(gen)  # Prime the generator
for i in range(3):
    gen.send(i)
    result = next(gen)
    print(f"Sent {i}, got {result}")
