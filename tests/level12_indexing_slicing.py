"""
Level 12: Advanced Indexing and Slicing (Python 3.12)
Tests for negative indexing, complex slicing, and f-string slice syntax
"""

# Test 1: Basic negative indexing
print("=== Test 1: Basic Negative Indexing ===")
lst = [10, 20, 30, 40, 50]
print(f"List: {lst}")
print(f"lst[-1] = {lst[-1]}")  # 50
print(f"lst[-2] = {lst[-2]}")  # 40
print(f"lst[-5] = {lst[-5]}")  # 10

# Test 2: Negative index out of bounds
print("\n=== Test 2: Negative Index Out of Bounds ===")
try:
    result = lst[-6]
    print(f"ERROR: Should have raised IndexError, got {result}")
except IndexError as e:
    print(f"Correctly raised IndexError: {e}")

# Test 3: Basic slicing (F-STRING SLICE SYNTAX)
print("\n=== Test 3: Basic Slicing ===")
nums = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
print(f"nums[2:5] = {nums[2:5]}")      # [2, 3, 4]
print(f"nums[:5] = {nums[:5]}")        # [0, 1, 2, 3, 4]
print(f"nums[5:] = {nums[5:]}")        # [5, 6, 7, 8, 9]
print(f"nums[:] = {nums[:]}")          # Full copy

# Test 4: Negative slice indices (F-STRING SLICE SYNTAX)
print("\n=== Test 4: Negative Slice Indices ===")
print(f"nums[-5:-2] = {nums[-5:-2]}")  # [5, 6, 7]
print(f"nums[-5:] = {nums[-5:]}")      # [5, 6, 7, 8, 9]
print(f"nums[:-5] = {nums[:-5]}")      # [0, 1, 2, 3, 4]

# Test 5: Step slicing (F-STRING SLICE SYNTAX)
print("\n=== Test 5: Step Slicing ===")
print(f"nums[::2] = {nums[::2]}")      # [0, 2, 4, 6, 8]
print(f"nums[1::2] = {nums[1::2]}")    # [1, 3, 5, 7, 9]
print(f"nums[::3] = {nums[::3]}")      # [0, 3, 6, 9]
print(f"nums[2:8:2] = {nums[2:8:2]}")  # [2, 4, 6]

# Test 6: Negative step reverse (F-STRING SLICE SYNTAX)
print("\n=== Test 6: Negative Step (Reverse) ===")
print(f"nums[::-1] = {nums[::-1]}")    # [9, 8, 7, 6, 5, 4, 3, 2, 1, 0]
print(f"nums[::-2] = {nums[::-2]}")    # [9, 7, 5, 3, 1]
print(f"nums[8:2:-1] = {nums[8:2:-1]}")  # [8, 7, 6, 5, 4, 3]

# Test 7: Empty slices (F-STRING SLICE SYNTAX)
print("\n=== Test 7: Empty Slices ===")
print(f"nums[5:5] = {nums[5:5]}")      # []
print(f"nums[5:3] = {nums[5:3]}")      # [] (start > stop)
print(f"nums[3:5:-1] = {nums[3:5:-1]}")  # [] (wrong direction)

# Test 8: Out of bounds slices - CRITICAL BUG FIX TEST (F-STRING SLICE SYNTAX)
print("\n=== Test 8: Out of Bounds Slices ===")
print(f"nums[10:20] = {nums[10:20]}")    # [] (not [9]!)
print(f"nums[100:200] = {nums[100:200]}")  # [] (not [9]!)
print(f"nums[-100:5] = {nums[-100:5]}")    # [0, 1, 2, 3, 4]
print(f"nums[5:100] = {nums[5:100]}")      # [5, 6, 7, 8, 9]

# Test 9: Tuple negative indexing
print("\n=== Test 9: Tuple Negative Indexing ===")
tup = (100, 200, 300, 400, 500)
print(f"tup[-1] = {tup[-1]}")  # 500
print(f"tup[-3] = {tup[-3]}")  # 300

# Test 10: String negative indexing
print("\n=== Test 10: String Negative Indexing ===")
s = "Python"
print(f's[-1] = {s[-1]}')  # 'n'
print(f's[-6] = {s[-6]}')  # 'P'

# Test 11: String slicing
print("\n=== Test 11: String Slicing ===")
text = "Hello World"
print(f'text[0:5] = {text[0:5]}')      # "Hello"
print(f'text[6:] = {text[6:]}')        # "World"
print(f'text[::-1] = {text[::-1]}')    # "dlroW olleH"
print(f'text[::2] = {text[::2]}')      # "HloWrd"

# Test 12: List assignment with negative index
print("\n=== Test 12: Assignment with Negative Index ===")
arr = [1, 2, 3, 4, 5]
arr[-1] = 99
arr[-2] = 88
print(f"After arr[-1]=99, arr[-2]=88: {arr}")  # [1, 2, 3, 88, 99]

# Test 13: Complex slice patterns
print("\n=== Test 13: Complex Slice Patterns ===")
data = list(range(20))
print(f"data[::5] = {data[::5]}")          # [0, 5, 10, 15]
print(f"data[2::3] = {data[2::3]}")        # [2, 5, 8, 11, 14, 17]
print(f"data[-10:-1:2] = {data[-10:-1:2]}")  # [10, 12, 14, 16, 18]

# Test 14: Edge case - single element list
print("\n=== Test 14: Single Element List ===")
single = [42]
print(f"single[-1] = {single[-1]}")    # 42
print(f"single[0:1] = {single[0:1]}")  # [42]
print(f"single[1:] = {single[1:]}")    # []

# Test 15: Edge case - empty list
print("\n=== Test 15: Empty List ===")
empty = []
print(f"empty[:] = {empty[:]}")        # []
print(f"empty[0:0] = {empty[0:0]}")    # []
try:
    result = empty[0]
    print(f"ERROR: Should have raised IndexError, got {result}")
except IndexError as e:
    print(f"Correctly raised IndexError for empty[0]")

print("\n=== All Tests Completed ===")
