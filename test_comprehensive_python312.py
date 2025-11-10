#!/usr/bin/env python3
"""
Comprehensive Python 3.12 Feature Test Suite
Tests all major Python 3.12 features to identify missing functionality
"""

print("=" * 70)
print("COMPREHENSIVE PYTHON 3.12 FEATURE TEST")
print("=" * 70)

# Track test results
passed = []
failed = []
skipped = []

def test_feature(name, test_func):
    """Run a test and track results"""
    try:
        test_func()
        passed.append(name)
        print(f"✅ {name}")
    except Exception as e:
        failed.append((name, str(e)))
        print(f"❌ {name}: {e}")

# Test 1: F-string features
def test_fstrings():
    # Basic f-strings
    x = 42
    assert f"{x}" == "42"
    assert f"{x:05d}" == "00042"
    # Nested f-strings
    assert f"{f'{x}'}" == "42"
    # Expression in f-strings
    assert f"{x * 2}" == "84"

test_feature("F-string features", test_fstrings)

# Test 2: Match statement
def test_match():
    def classify(value):
        match value:
            case int(x) if x > 0:
                return "positive int"
            case int(x) if x < 0:
                return "negative int"
            case 0:
                return "zero"
            case str():
                return "string"
            case _:
                return "other"

    assert classify(5) == "positive int"
    assert classify(-3) == "negative int"
    assert classify(0) == "zero"
    assert classify("hello") == "string"

test_feature("Match statement", test_match)

# Test 3: Walrus operator
def test_walrus():
    # In if statement
    if (n := 10) > 5:
        assert n == 10
    # In while loop
    data = [1, 2, 3, 4, 5]
    result = []
    i = 0
    while (val := data[i] if i < len(data) else None) is not None:
        result.append(val * 2)
        i += 1
    assert result == [2, 4, 6, 8, 10]

test_feature("Walrus operator", test_walrus)

# Test 4: Type hints and annotations
def test_type_hints():
    def greet(name: str) -> str:
        return f"Hello, {name}"
    assert greet("World") == "Hello, World"
    # Function annotations
    assert greet.__annotations__ == {'name': str, 'return': str}

test_feature("Type hints", test_type_hints)

# Test 5: Comprehensions
def test_comprehensions():
    # List comprehension
    assert [x * 2 for x in range(5)] == [0, 2, 4, 6, 8]
    # Dict comprehension
    assert {x: x**2 for x in range(3)} == {0: 0, 1: 1, 2: 4}
    # Set comprehension
    assert {x % 3 for x in range(6)} == {0, 1, 2}
    # Generator expression
    assert list(x * 2 for x in range(3)) == [0, 2, 4]
    # Nested comprehension
    assert [[i*j for j in range(2)] for i in range(2)] == [[0, 0], [0, 1]]

test_feature("Comprehensions", test_comprehensions)

# Test 6: Iterator functions
def test_iterators():
    # enumerate
    assert list(enumerate(['a', 'b'])) == [(0, 'a'), (1, 'b')]
    # zip
    assert list(zip([1, 2], ['a', 'b'])) == [(1, 'a'), (2, 'b')]
    # map
    assert list(map(lambda x: x*2, [1, 2, 3])) == [2, 4, 6]
    # filter
    assert list(filter(lambda x: x%2==0, [1,2,3,4])) == [2, 4]
    # reversed
    assert list(reversed([1, 2, 3])) == [3, 2, 1]

test_feature("Iterator functions", test_iterators)

# Test 7: String methods
def test_string_methods():
    s = "hello"
    assert s.upper() == "HELLO"
    assert s.capitalize() == "Hello"
    assert s.replace("l", "L") == "heLLo"
    assert "he" in s
    assert s.startswith("hel")
    assert s.endswith("lo")
    assert s.split("l") == ['he', '', 'o']
    assert "-".join(["a", "b", "c"]) == "a-b-c"

test_feature("String methods", test_string_methods)

# Test 8: List methods
def test_list_methods():
    lst = [1, 2, 3]
    lst.append(4)
    assert lst == [1, 2, 3, 4]
    lst.extend([5, 6])
    assert lst == [1, 2, 3, 4, 5, 6]
    lst.insert(0, 0)
    assert lst == [0, 1, 2, 3, 4, 5, 6]
    assert lst.pop() == 6
    lst.remove(0)
    assert lst == [1, 2, 3, 4, 5]

test_feature("List methods", test_list_methods)

# Test 9: Dict methods
def test_dict_methods():
    d = {'a': 1, 'b': 2}
    assert d.get('a') == 1
    assert d.get('c', 3) == 3
    assert list(d.keys()) == ['a', 'b']
    assert list(d.values()) == [1, 2]
    assert list(d.items()) == [('a', 1), ('b', 2)]
    d.update({'c': 3})
    assert d == {'a': 1, 'b': 2, 'c': 3}

test_feature("Dict methods", test_dict_methods)

# Test 10: Set operations
def test_set_operations():
    s1 = {1, 2, 3}
    s2 = {2, 3, 4}
    assert s1 | s2 == {1, 2, 3, 4}  # union
    assert s1 & s2 == {2, 3}  # intersection
    assert s1 - s2 == {1}  # difference
    assert s1 ^ s2 == {1, 4}  # symmetric difference

test_feature("Set operations", test_set_operations)

# Test 11: Lambda functions
def test_lambda():
    f = lambda x: x * 2
    assert f(5) == 10
    # Lambda in sorted
    data = [(2, 'b'), (1, 'a'), (3, 'c')]
    assert sorted(data, key=lambda x: x[0]) == [(1, 'a'), (2, 'b'), (3, 'c')]

test_feature("Lambda functions", test_lambda)

# Test 12: Decorators
def test_decorators():
    def double(func):
        def wrapper(*args, **kwargs):
            return func(*args, **kwargs) * 2
        return wrapper

    @double
    def add(a, b):
        return a + b

    assert add(2, 3) == 10  # (2+3)*2

test_feature("Decorators", test_decorators)

# Test 13: Context managers
def test_context_managers():
    class MyContext:
        def __init__(self):
            self.entered = False
            self.exited = False
        def __enter__(self):
            self.entered = True
            return self
        def __exit__(self, *args):
            self.exited = True
            return False

    ctx = MyContext()
    with ctx:
        assert ctx.entered == True
    assert ctx.exited == True

test_feature("Context managers", test_context_managers)

# Test 14: Generators
def test_generators():
    def gen():
        yield 1
        yield 2
        yield 3

    assert list(gen()) == [1, 2, 3]
    # Generator expression
    g = (x*2 for x in range(3))
    assert list(g) == [0, 2, 4]

test_feature("Generators", test_generators)

# Test 15: Class features
def test_classes():
    class Animal:
        def __init__(self, name):
            self.name = name
        def speak(self):
            return f"{self.name} speaks"

    class Dog(Animal):
        def speak(self):
            return f"{self.name} barks"

    dog = Dog("Buddy")
    assert dog.speak() == "Buddy barks"
    assert isinstance(dog, Dog)
    assert isinstance(dog, Animal)

test_feature("Class features", test_classes)

# Test 16: Property decorator
def test_property():
    class Circle:
        def __init__(self, radius):
            self._radius = radius

        @property
        def radius(self):
            return self._radius

        @radius.setter
        def radius(self, value):
            self._radius = value

        @property
        def area(self):
            return 3.14159 * self._radius ** 2

    c = Circle(5)
    assert c.radius == 5
    assert abs(c.area - 78.53975) < 0.001
    c.radius = 10
    assert c.radius == 10

test_feature("Property decorator", test_property)

# Test 17: Class and static methods
def test_class_static_methods():
    class Math:
        multiplier = 2

        @classmethod
        def multiply_by_class(cls, x):
            return x * cls.multiplier

        @staticmethod
        def add(a, b):
            return a + b

    assert Math.multiply_by_class(5) == 10
    assert Math.add(3, 4) == 7

test_feature("Class and static methods", test_class_static_methods)

# Test 18: Try-except-finally
def test_exception_handling():
    result = []
    try:
        result.append("try")
        x = 1 / 1
    except ZeroDivisionError:
        result.append("except")
    finally:
        result.append("finally")
    assert result == ["try", "finally"]

    # Test with exception
    result = []
    try:
        result.append("try")
        x = 1 / 0
    except ZeroDivisionError:
        result.append("except")
    finally:
        result.append("finally")
    assert result == ["try", "except", "finally"]

test_feature("Exception handling", test_exception_handling)

# Print summary
print("\n" + "=" * 70)
print("TEST SUMMARY")
print("=" * 70)
print(f"✅ Passed: {len(passed)}")
print(f"❌ Failed: {len(failed)}")
print(f"⏭️  Skipped: {len(skipped)}")

if failed:
    print("\nFailed tests:")
    for name, error in failed:
        print(f"  • {name}")
        print(f"    Error: {error[:100]}...")

print("\n" + "=" * 70)
if len(failed) == 0:
    print("🎉 ALL TESTS PASSED!")
else:
    print(f"⚠️  {len(failed)} test(s) failed")
print("=" * 70)
