# Advanced Python Features Example
from abc import ABC, abstractmethod

# Abstract base class
class Animal(ABC):
    @abstractmethod
    def speak(self):
        pass

class Dog(Animal):
    def speak(self):
        return "Woof!"

class Cat(Animal):
    def speak(self):
        return "Meow!"

# Match statement (Python 3.10+)
def describe(obj):
    match obj:
        case Dog():
            print("It's a dog!")
        case Cat():
            print("It's a cat!")
        case _:
            print("Unknown animal")

# Using decorators
def uppercase_decorator(func):
    def wrapper(*args, **kwargs):
        result = func(*args, **kwargs)
        return result.upper()
    return wrapper

@uppercase_decorator
def get_message(name):
    return f"hello, {name}"

# Main execution
if __name__ == "__main__":
    dog = Dog()
    cat = Cat()

    print(dog.speak())
    print(cat.speak())

    describe(dog)
    describe(cat)

    print(get_message("world"))
