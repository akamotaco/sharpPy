"""
Level 3: Variables and Assignment Test
Tests: simple assignment, multiple assignment, augmented assignment
"""

# Simple assignment
print("=== Simple Assignment ===")
x = 10
print("x =", x)

# Multiple assignment
print("\n=== Multiple Assignment ===")
a, b = 1, 2
print("a, b =", a, b)

c = d = 5
print("c = d =", c, d)

# Augmented assignment
print("\n=== Augmented Assignment ===")
x = 10
print("x =", x)

x += 5
print("x += 5 ->", x)

x -= 3
print("x -= 3 ->", x)

x *= 2
print("x *= 2 ->", x)

x //= 4
print("x //= 4 ->", x)

# Sequence unpacking
print("\n=== Sequence Unpacking ===")
nums = [1, 2, 3]
p, q, r = nums
print("p, q, r =", p, q, r)
