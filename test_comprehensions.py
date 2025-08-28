# Test Python comprehensions
# List comprehension
numbers = [1, 2, 3, 4, 5]
doubled = [x * 2 for x in numbers]
print(f"Doubled: {doubled}")

# Dictionary comprehension  
squares = {x: x*x for x in range(1, 6)}
print(f"Squares: {squares}")

# Set comprehension
evens = {x for x in range(10) if x % 2 == 0}  
print(f"Evens: {evens}")

# Nested comprehension
matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
flattened = [x for row in matrix for x in row]
print(f"Flattened: {flattened}")