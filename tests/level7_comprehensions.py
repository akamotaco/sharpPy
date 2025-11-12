"""
Level 7: Comprehensions Test
Tests: list comprehension, dict comprehension, set comprehension
"""

# List comprehension
print("=== List Comprehension ===")
squares = [x**2 for x in range(5)]
print("Squares:", squares)

evens = [x for x in range(10) if x % 2 == 0]
print("Evens:", evens)

# Nested list comprehension
matrix = [[i+j for j in range(3)] for i in range(3)]
print("Matrix:", matrix)

# Dict comprehension
print("\n=== Dict Comprehension ===")
square_dict = {x: x**2 for x in range(5)}
print("Square dict:", square_dict)

# Set comprehension
print("\n=== Set Comprehension ===")
unique_squares = {x**2 for x in [1, 2, 2, 3, 3, 4]}
print("Unique squares:", unique_squares)

# Complex comprehension
print("\n=== Complex Comprehension ===")
words = ["hello", "world", "python"]
lengths = [len(w) for w in words]
print("Word lengths:", lengths)

# Comprehension with multiple conditions
filtered = [x for x in range(20) if x % 2 == 0 if x % 3 == 0]
print("Divisible by 2 and 3:", filtered)
