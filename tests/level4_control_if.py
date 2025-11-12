"""
Level 4: Control Flow - If/Elif/Else Test
Tests: if, elif, else, nested conditions
"""

# Simple if
print("=== Simple If ===")
x = 10
if x > 5:
    print("x is greater than 5")

# If-else
print("\n=== If-Else ===")
y = 3
if y > 5:
    print("y is greater than 5")
else:
    print("y is not greater than 5")

# If-elif-else
print("\n=== If-Elif-Else ===")
z = 5
if z > 5:
    print("z is greater than 5")
elif z == 5:
    print("z is equal to 5")
else:
    print("z is less than 5")

# Nested if
print("\n=== Nested If ===")
a = 10
b = 20
if a > 5:
    if b > 15:
        print("Both conditions true")
    else:
        print("Only first condition true")
else:
    print("First condition false")

# Multiple conditions
print("\n=== Multiple Conditions ===")
c = 15
if c > 10 and c < 20:
    print("c is between 10 and 20")

if c == 15 or c == 20:
    print("c is 15 or 20")
