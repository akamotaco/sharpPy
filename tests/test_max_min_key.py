"""
Test: max() / min() with key parameter
CPython behavior: key= transforms values for comparison, never compares original objects directly.
"""

# --- 1. key with simple objects ---
print("=== max/min with key (basic) ===")
nums = [3, -5, 2, -1, 4]
print("max by abs:", max(nums, key=lambda x: abs(x)))   # -5
print("min by abs:", min(nums, key=lambda x: abs(x)))   # -1

words = ["banana", "pie", "strawberry", "fig"]
print("max by len:", max(words, key=lambda w: len(w)))   # strawberry
print("min by len:", min(words, key=lambda w: len(w)))   # pie (or fig)

# --- 2. key with custom class (no __gt__/__lt__) ---
print("\n=== max/min with key on non-comparable class ===")

class Item:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __repr__(self):
        return f"Item({self.name}, {self.value})"

items = [Item("a", 10), Item("b", 30), Item("c", 20)]
best = max(items, key=lambda i: i.value)
print("max item:", best)  # Item(b, 30)
worst = min(items, key=lambda i: i.value)
print("min item:", worst)  # Item(a, 10)

# --- 3. key with tied values (tiebreaker = first occurrence, no object comparison) ---
print("\n=== tied key values ===")
items2 = [Item("x", 5), Item("y", 5), Item("z", 5)]
result = max(items2, key=lambda i: i.value)
print("tied max:", result)  # Item(x, 5) — first occurrence wins

result2 = min(items2, key=lambda i: i.value)
print("tied min:", result2)  # Item(x, 5) — first occurrence wins

# --- 4. default parameter ---
print("\n=== default parameter ===")
empty = []
d = max(empty, key=lambda x: x, default=-1)
print("max empty default:", d)  # -1
d2 = min(empty, key=lambda x: x, default=99)
print("min empty default:", d2)  # 99

# --- 5. multi-arg max/min with key ---
print("\n=== multi-arg with key ===")
print("max(3,-5,2, key=abs):", max(3, -5, 2, key=abs))  # -5
print("min(3,-5,2, key=abs):", min(3, -5, 2, key=abs))  # 2

print("\nAll tests passed!")
