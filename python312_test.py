# Testing Python 3.12 Compatibility
# Basic type operations
x = 42
y = 3.14
z = 'hello'
print(type(x), type(y), type(z))

# List comprehension
numbers = [i**2 for i in range(5)]
print(numbers)

# Dictionary operations
d = {'a': 1, 'b': 2}
print(d.keys())

# Exception handling
try:
    result = 10 / 0
except ZeroDivisionError:
    print('Division by zero caught')
