"""
Level 4: Control Flow - Loops Test
Tests: while, for, break, continue, range
"""

# While loop
print("=== While Loop ===")
i = 0
while i < 3:
    print("i =", i)
    i += 1

# For loop with range
print("\n=== For Loop ===")
for j in range(3):
    print("j =", j)

# For loop with list
print("\n=== For Loop with List ===")
fruits = ["apple", "banana", "cherry"]
for fruit in fruits:
    print(fruit)

# Break statement
print("\n=== Break ===")
for k in range(10):
    if k == 5:
        break
    print("k =", k)

# Continue statement
print("\n=== Continue ===")
for m in range(5):
    if m == 2:
        continue
    print("m =", m)

# Nested loops
print("\n=== Nested Loops ===")
for x in range(2):
    for y in range(2):
        print(f"({x}, {y})")

# While with break
print("\n=== While with Break ===")
n = 0
while True:
    print("n =", n)
    n += 1
    if n >= 3:
        break
