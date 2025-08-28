# Test built-in functions and math/random modules
print("=== Testing Built-in Functions ===")

# Test range function
print("range(5):", list(range(5)))
print("range(2, 8):", list(range(2, 8)))
print("range(0, 10, 2):", list(range(0, 10, 2)))

# Test len function
test_list = [1, 2, 3, 4, 5]
print("len([1,2,3,4,5]):", len(test_list))
print("len('hello'):", len("hello"))

# Test enumerate function
print("enumerate(['a','b','c']):", list(enumerate(['a', 'b', 'c'])))

# Test zip function
list1 = [1, 2, 3]
list2 = ['a', 'b', 'c']
print("zip([1,2,3], ['a','b','c']):", list(zip(list1, list2)))

# Test map function
numbers = [1, 2, 3, 4, 5]
print("map(lambda x: x*2, [1,2,3,4,5]):", list(map(lambda x: x*2, numbers)))

# Test filter function
print("filter(lambda x: x%2==0, [1,2,3,4,5]):", list(filter(lambda x: x%2==0, numbers)))

# Test sorted function
unsorted_list = [3, 1, 4, 1, 5, 9, 2]
print("sorted([3,1,4,1,5,9,2]):", sorted(unsorted_list))

# Test type conversion functions
print("int('42'):", int('42'))
print("float('3.14'):", float('3.14'))
print("str(123):", str(123))
print("bool(1):", bool(1))
print("bool(0):", bool(0))

# Test isinstance and type functions
print("isinstance(42, int):", isinstance(42, int))
print("type(42):", type(42))

print("\n=== Testing Math Module ===")
import math

# Test math constants
print("math.pi:", math.pi)
print("math.e:", math.e)  
print("math.tau:", math.tau)

# Test basic math functions
print("math.sqrt(16):", math.sqrt(16))
print("math.pow(2, 3):", math.pow(2, 3))
print("math.abs(-5):", math.abs(-5))

# Test trigonometric functions
print("math.sin(math.pi/2):", math.sin(math.pi/2))
print("math.cos(0):", math.cos(0))
print("math.tan(math.pi/4):", math.tan(math.pi/4))

# Test logarithmic functions
print("math.log(math.e):", math.log(math.e))
print("math.log10(100):", math.log10(100))
print("math.log2(8):", math.log2(8))

# Test ceiling and floor
print("math.ceil(3.2):", math.ceil(3.2))
print("math.floor(3.8):", math.floor(3.8))

# Test factorial
print("math.factorial(5):", math.factorial(5))

print("\n=== Testing Random Module ===")
import random

# Test basic random functions
print("random.random():", random.random())
print("random.randint(1, 10):", random.randint(1, 10))
print("random.uniform(1.0, 2.0):", random.uniform(1.0, 2.0))

# Test choice function
choices = ['apple', 'banana', 'cherry']
print("random.choice(['apple','banana','cherry']):", random.choice(choices))

# Test randrange function
print("random.randrange(0, 10, 2):", random.randrange(0, 10, 2))

# Test gauss function (normal distribution)
print("random.gauss(0, 1):", random.gauss(0, 1))

# Test shuffle function (modifies list in place)
test_shuffle = [1, 2, 3, 4, 5]
random.shuffle(test_shuffle)
print("After random.shuffle([1,2,3,4,5]):", test_shuffle)

# Test sample function
sample_list = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
print("random.sample([1..10], 3):", random.sample(sample_list, 3))

print("\n=== All tests completed ===")