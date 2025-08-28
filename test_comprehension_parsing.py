# 컴프리헨션 파싱 테스트

# List comprehensions
numbers = [x * 2 for x in range(5)]
filtered = [x for x in range(10) if x % 2 == 0]
nested = [x * y for x in range(3) for y in range(2) if x + y > 1]

# Dictionary comprehensions
squares = {x: x**2 for x in range(5)}
word_lengths = {word: len(word) for word in ["hello", "world"] if len(word) > 4}

# Set comprehensions
unique_squares = {x**2 for x in range(-5, 6)}
filtered_set = {x for x in range(20) if x % 3 == 0}

# Generator expressions
gen_squares = (x**2 for x in range(5))
filtered_gen = (x for x in range(100) if x % 7 == 0)

# Complex comprehensions with unpacking
pairs = [(x, y) for x, y in [(1, 2), (3, 4), (5, 6)]]
matrix_flat = [item for row in [[1, 2], [3, 4]] for item in row]

print("Comprehension parsing test completed")