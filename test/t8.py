class Animal:
    def __init__(self, name):
        super().__init__()
        self.name = name
    
    def speak(self):
        return f"{self.name} makes a sound"

class Dog(Animal):
    def __init__(self, name, breed):
        super().__init__(name)  # Python 3 스타일
        self.breed = breed
    
    def speak(self):
        parent_speak = super().speak()  # 부모 메서드 호출
        return f"{parent_speak} - Woof!"

class Cat(Animal):
    def __init__(self, name, color):
        super(Cat, self).__init__(name)  # Python 2 스타일도 지원
        self.color = color
    
    def speak(self):
        return f"{self.name} says Meow!"

print(super)
print(dir(super))
# a = super(int)
animal = Animal('animal')
# 테스트
dog = Dog("Buddy", "Golden Retriever")
print(dog.speak())  # "Buddy makes a sound - Woof!"

cat = Cat("Whiskers", "orange")
print(cat.speak())  # "Whiskers says Meow!"