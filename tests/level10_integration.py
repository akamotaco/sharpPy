"""
Level 10: Integration Test
Tests: Combination of multiple features
"""

# Function using comprehensions and exceptions
def safe_divide(numbers, divisor):
    try:
        return [n / divisor for n in numbers]
    except ZeroDivisionError:
        return "Cannot divide by zero"

print("=== Function + Comprehension + Exception ===")
print(safe_divide([10, 20, 30], 2))
print(safe_divide([10, 20, 30], 0))

# Class with generator
class NumberGenerator:
    def __init__(self, limit):
        self.limit = limit

    def generate(self):
        for i in range(self.limit):
            yield i ** 2

print("\n=== Class + Generator ===")
gen = NumberGenerator(5)
for num in gen.generate():
    print(num, end=" ")
print()

# Nested structures
data = {
    "users": [
        {"name": "Alice", "age": 25},
        {"name": "Bob", "age": 30}
    ]
}

print("\n=== Nested Data Structures ===")
for user in data["users"]:
    print(f"{user['name']}: {user['age']}")

# Function with default args and kwargs
def process_data(data, prefix="", suffix="", **options):
    result = f"{prefix}{data}{suffix}"
    if options.get("upper"):
        result = result.upper()
    if options.get("reverse"):
        result = result[::-1]
    return result

print("\n=== Complex Function Call ===")
print(process_data("hello"))
print(process_data("hello", prefix="<", suffix=">"))
print(process_data("hello", upper=True))
print(process_data("hello", prefix="[", suffix="]", upper=True, reverse=True))
