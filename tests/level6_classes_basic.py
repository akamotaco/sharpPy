"""
Level 6: Classes - Basic Test
Tests: class, __init__, methods, attributes, self
"""

# Simple class
class Dog:
    def __init__(self, name):
        self.name = name

    def bark(self):
        print(f"{self.name} says Woof!")

print("=== Simple Class ===")
dog1 = Dog("Buddy")
dog1.bark()

# Class with multiple attributes
class Person:
    def __init__(self, name, age):
        self.name = name
        self.age = age

    def introduce(self):
        print(f"I am {self.name}, {self.age} years old")

print("\n=== Multiple Attributes ===")
person1 = Person("Alice", 25)
person1.introduce()

# Class with methods
class Calculator:
    def add(self, a, b):
        return a + b

    def multiply(self, a, b):
        return a * b

print("\n=== Class Methods ===")
calc = Calculator()
print("add(5, 3) =", calc.add(5, 3))
print("multiply(4, 6) =", calc.multiply(4, 6))

# Class with instance variables
class Counter:
    def __init__(self):
        self.count = 0

    def increment(self):
        self.count += 1

    def get_count(self):
        return self.count

print("\n=== Instance Variables ===")
counter = Counter()
counter.increment()
counter.increment()
print("Count:", counter.get_count())
