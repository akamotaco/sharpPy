# 상속 관계 테스트
class Animal:
    def speak(self) -> str:
        return "Some sound"

class Dog(Animal):
    def speak(self) -> str:
        return "Woof!"

class Cat(Animal):
    def speak(self) -> str:
        return "Meow!"

class Puppy(Dog):  # Dog를 상속
    def speak(self) -> str:
        return "Yip!"

# 타입 힌트 테스트
def feed_animal(animal: Animal) -> str:
    return f"Feeding {animal.__class__.__name__}"

# 모두 정상 동작해야 함
print(feed_animal(Animal()))  # OK
print(feed_animal(Dog()))     # OK - Dog는 Animal의 자식
print(feed_animal(Cat()))     # OK - Cat도 Animal의 자식  
print(feed_animal(Puppy()))   # OK - Puppy는 Dog의 자식이고, Dog는 Animal의 자식

# 다중 상속 테스트
class Flyable:
    def fly(self) -> str:
        return "Flying"

class Bird(Animal, Flyable):
    def speak(self) -> str:
        return "Tweet!"

def process_animal(animal: Animal) -> None:
    print(animal.speak())

def process_flyable(obj: Flyable) -> None:
    print(obj.fly())

bird = Bird()
process_animal(bird)   # OK - Bird는 Animal을 상속
process_flyable(bird)  # OK - Bird는 Flyable도 상속

# 잘못된 타입 테스트
class Robot:
    def compute(self) -> int:
        return 42

def pet_animal(animal: Animal) -> None:
    print(f"Petting {animal.__class__.__name__}")

robot = Robot()
pet_animal(robot)  # TypeError! Robot은 Animal을 상속하지 않음