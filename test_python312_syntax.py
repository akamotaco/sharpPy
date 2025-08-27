# Python 3.12 종합 문법 테스트

# ===== 1. Type Parameters (PEP 695) =====
type Vector[T] = list[T]
type Matrix[T] = list[list[T]]
type StringDict[V] = dict[str, V]

# Generic functions with type parameters
def process[T](items: T) -> T:
    return items

def combine[T, U](first: T, second: U) -> tuple[T, U]:
    return (first, second)

# Generic classes with type parameters  
class Container[T]:
    def __init__(self, value: T):
        self.value = value

class GenericClass[T: int, *Ts, **P]:
    pass

# ===== 2. Match Statement (Python 3.10+) =====
def handle_value(value):
    match value:
        case int() if value > 0:
            return "positive integer"
        case str() as s if len(s) > 0:
            return f"non-empty string: {s}"
        case [x, y] if x == y:
            return f"pair of equal values: {x}"
        case {'key': value}:
            return f"dict with key: {value}"
        case _:
            return "other"

# ===== 3. Walrus Operator (:=) =====
def walrus_examples():
    # In if statements
    if (n := len("hello")) > 3:
        print(f"Length is {n}")
    
    # In list comprehensions
    data = [y := x*2 for x in range(5)]
    
    # In while loops
    while (line := input()) != "quit":
        print(f"You said: {line}")

# ===== 4. Async/Await =====
async def async_function():
    await some_coroutine()
    async for item in async_iterator():
        yield item

async def async_generator():
    async with async_context_manager():
        yield "value"

# ===== 5. Advanced Function Features =====
def function_with_annotations(
    pos_only: int, /,
    regular: str,
    *args: int,
    kw_only: bool = True,
    **kwargs: str
) -> dict[str, any]:
    return {}

# Lambda with type parameters (hypothetical)
lambda_func = lambda x, y: x + y

# ===== 6. Advanced Collections =====
def collection_examples():
    # Lists, tuples, sets, dicts
    my_list = [1, 2, 3, 4, 5]
    my_tuple = (1, 2, 3)
    my_set = {1, 2, 3, 4}
    my_dict = {"a": 1, "b": 2}
    
    # Nested collections
    nested = [[1, 2], [3, 4]]
    
    # Comprehensions
    list_comp = [x*2 for x in range(10) if x % 2 == 0]
    dict_comp = {str(x): x*2 for x in range(5)}
    set_comp = {x*2 for x in range(10)}
    gen_comp = (x*2 for x in range(10))

# ===== 7. String Features =====
def string_features():
    name = "World"
    
    # f-strings
    basic_f = f"Hello, {name}!"
    complex_f = f"Result: {2 + 3 = }"
    nested_f = f"Outer {f'inner {name}'}"
    
    # Raw strings
    raw_string = r"C:\path\to\file"
    
    # Triple quotes
    multiline = """
    This is a
    multiline string
    """

# ===== 8. Exception Handling =====
def exception_examples():
    try:
        risky_operation()
    except ValueError as e:
        handle_value_error(e)
    except (TypeError, AttributeError):
        handle_type_or_attr_error()
    except Exception:
        handle_generic_error()
    else:
        print("No exception occurred")
    finally:
        cleanup()
    
    # Raise statements
    raise ValueError("Something went wrong")
    raise

# ===== 9. Context Managers =====
def context_manager_examples():
    # Basic with statement
    with open("file.txt") as f:
        content = f.read()
    
    # Multiple context managers
    with open("input.txt") as infile, open("output.txt", "w") as outfile:
        outfile.write(infile.read())

# ===== 10. Advanced Control Flow =====
def control_flow_examples():
    # For loops with else
    for i in range(10):
        if i == 5:
            break
    else:
        print("No break occurred")
    
    # While loops
    i = 0
    while i < 10:
        i += 1
        if i == 5:
            continue
        print(i)
    
    # Nested loops
    for i in range(3):
        for j in range(3):
            if i == j:
                break

# ===== 11. Operators =====
def operator_examples():
    # Arithmetic
    a = 10 + 5 - 3 * 2 / 1
    b = 10 ** 2
    c = 17 // 3
    d = 17 % 3
    
    # Bitwise
    x = 5 & 3
    y = 5 | 3
    z = 5 ^ 3
    w = ~5
    u = 5 << 1
    v = 5 >> 1
    
    # Comparison
    comparisons = [
        1 < 2, 1 <= 2, 1 > 2, 1 >= 2,
        1 == 2, 1 != 2,
        "a" in "abc", "d" not in "abc",
        [] is None, [] is not None
    ]
    
    # Logical
    logical = True and False or not True
    
    # Assignment operators
    x = 10
    x += 5
    x -= 2
    x *= 3
    x /= 2
    x //= 2
    x %= 3
    x **= 2
    x &= 7
    x |= 1
    x ^= 3
    x <<= 1
    x >>= 1

# ===== 12. Variable Management =====
def variable_management():
    global global_var
    nonlocal nonlocal_var
    
    # Delete statements
    x = [1, 2, 3]
    del x[0]
    del x
    
    # Unpacking
    a, b, c = [1, 2, 3]
    first, *middle, last = [1, 2, 3, 4, 5]
    x, y = y, x  # Swap

# ===== 13. Import Statements =====
import os
import sys as system
from pathlib import Path
from collections import defaultdict, Counter
from typing import *

# ===== 14. Class Features =====
class AdvancedClass[T]:
    class_var = "shared"
    
    def __init__(self, value: T):
        self.value = value
    
    @property
    def prop(self):
        return self._prop
    
    @prop.setter
    def prop(self, value):
        self._prop = value
    
    @classmethod
    def create_empty(cls):
        return cls(None)
    
    @staticmethod
    def static_method():
        return "static"
    
    def __str__(self):
        return f"AdvancedClass({self.value})"
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        pass

# Multiple inheritance
class Child(AdvancedClass, dict):
    pass

# ===== 15. Decorators =====
def decorator(func):
    def wrapper(*args, **kwargs):
        return func(*args, **kwargs)
    return wrapper

@decorator
def decorated_function():
    pass

# ===== 16. Generators and Yield =====
def simple_generator():
    yield 1
    yield 2
    yield 3

def generator_with_send():
    value = None
    while True:
        received = yield value
        if received is not None:
            value = received

def yield_from_example():
    yield from range(5)
    yield from [10, 20, 30]

# ===== 17. Advanced Expressions =====
def advanced_expressions():
    # Conditional expressions
    result = "positive" if 5 > 0 else "negative"
    
    # Slicing
    data = [1, 2, 3, 4, 5]
    subset = data[1:4]
    reversed_data = data[::-1]
    stepped = data[::2]
    
    # Attribute access
    obj.attr.nested_attr
    
    # Method chaining
    "hello".upper().replace("E", "3")

# ===== 18. Assert Statements =====
def assertion_examples():
    assert True
    assert 1 == 1, "Math is broken"
    assert callable(print), "print should be callable"

# ===== Main Test Function =====
def main():
    """Test all Python 3.12 features."""
    print("Testing Python 3.12 syntax features...")
    
    # This would test our parser's ability to handle all constructs
    test_cases = [
        "type Vector[T] = list[T]",
        "def func[T](x: T) -> T: return x",
        "class Generic[T]: pass",
        "match x: case 1: pass",
        "if (n := len('test')) > 0: pass",
        "async def f(): await something()",
        "lambda x, y: x + y",
        "[x for x in range(10) if x % 2 == 0]",
        "{'a': 1, 'b': 2}",
        "f'Hello {name}!'",
        "with open('file') as f: pass",
        "try: pass\nexcept: pass\nfinally: pass",
        "x += 5",
        "yield from generator()",
        "assert condition, 'message'",
        "del variable",
        "global x",
        "nonlocal y",
        "import module",
        "from package import item"
    ]
    
    return "All syntax features defined!"

if __name__ == "__main__":
    main()