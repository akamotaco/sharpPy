import copy

# 1. 리스트의 얕은 복사 vs 깊은 복사
original = [[1, 2, 3], [4, 5, 6]]

shallow = copy.copy(original)
deep = copy.deepcopy(original)

# 내부 리스트 수정
original[0][0] = 999

print(f"Original: {original}")  # [[999, 2, 3], [4, 5, 6]]
print(f"Shallow: {shallow}")    # [[999, 2, 3], [4, 5, 6]] - 영향받음!
print(f"Deep: {deep}")          # [[1, 2, 3], [4, 5, 6]] - 영향없음

# 2. 딕셔너리 테스트
original_dict = {"a": [1, 2], "b": {"nested": 3}}

shallow_dict = copy.copy(original_dict)
deep_dict = copy.deepcopy(original_dict)

original_dict["a"][0] = 100
original_dict["b"]["nested"] = 999

print(f"Original dict: {original_dict}")  # {"a": [100, 2], "b": {"nested": 999}}
print(f"Shallow dict: {shallow_dict}")    # {"a": [100, 2], "b": {"nested": 999}}
print(f"Deep dict: {deep_dict}")          # {"a": [1, 2], "b": {"nested": 3}}

# 3. 클래스 인스턴스 테스트
class Person:
    def __init__(self, name, items):
        self.name = name
        self.items = items

person1 = Person("Alice", [1, 2, 3])
person2 = copy.copy(person1)
person3 = copy.deepcopy(person1)

person1.items.append(4)
person1.name = "Bob"

print(f"Original items: {person1.items}, name: {person1.name}")  # [1,2,3,4], Bob
print(f"Shallow items: {person2.items}, name: {person2.name}")   # [1,2,3,4], Alice
print(f"Deep items: {person3.items}, name: {person3.name}")      # [1,2,3], Alice