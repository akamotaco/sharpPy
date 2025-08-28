# Python 3.12 호환성 종합 테스트

# ===== 1. PEP 695: Type Parameters =====
# 새로운 제네릭 문법
type Vector[T] = list[T]
type Matrix[T, U] = dict[T, U]

# 제네릭 함수
def process[T](item: T) -> T:
    return item

def combine[T, U](a: T, b: U) -> tuple[T, U]:
    return (a, b)

# 제네릭 클래스
class Container[T]:
    def __init__(self, value: T):
        self.value = value
    
    def get(self) -> T:
        return self.value

# ===== 2. Structural Pattern Matching =====
def analyze_data(data):
    match data:
        case int(x) if x > 100:
            return "large number"
        case str(s) if len(s) > 10:
            return "long string"
        case [x, y] if x == y:
            return "equal pair"
        case {"key": value, **rest}:
            return f"dict with key={value}"
        case _:
            return "unknown"

# ===== 3. Walrus Operator (:=) =====
def walrus_examples():
    # Basic assignment expressions
    if (length := len("hello")) > 3:
        print(f"Length is {length}")
    
    # In list comprehensions
    data = [squared := x*2 for x in range(3)]
    
    # In while loops
    numbers = [1, 2, 3, 0, 4]
    i = 0
    while (num := numbers[i]) != 0:
        print(num)
        i += 1

# ===== 4. f-strings improvements =====
def fstring_examples():
    name = "Python"
    version = 3.12
    
    # Basic f-string
    basic = f"Hello {name}!"
    
    # Expressions in f-strings
    calc = f"{2 + 3 = }"
    
    # Format specifiers
    pi = 3.14159
    formatted = f"{pi:.2f}"
    
    # Nested f-strings (Python 3.12)
    nested = f"{'Hello' if True else 'Hi'} {name}!"
    
    return basic, calc, formatted, nested

# ===== 5. Enhanced Error Messages =====
def error_examples():
    try:
        # This should give better error messages in Python 3.12
        d = {}
        value = d["missing_key"]
    except KeyError as e:
        print(f"KeyError: {e}")
    
    try:
        # Better traceback information
        def nested_error():
            raise ValueError("Something went wrong")
        nested_error()
    except ValueError as e:
        print(f"ValueError: {e}")

# ===== 6. Performance improvements =====
def performance_examples():
    # List comprehensions (optimized in 3.12)
    squares = [x*x for x in range(1000)]
    
    # Dictionary comprehensions
    square_dict = {x: x*x for x in range(100)}
    
    # Set comprehensions
    unique_squares = {x*x for x in range(100)}
    
    return len(squares), len(square_dict), len(unique_squares)

# ===== 7. Async improvements =====
async def async_examples():
    import asyncio
    
    # Async comprehensions
    async def async_generator():
        for i in range(3):
            yield i
    
    # Async for loops
    result = []
    async for item in async_generator():
        result.append(item)
    
    return result

# ===== 8. Context managers improvements =====
def context_manager_examples():
    import contextlib
    
    # Multiple context managers
    with open("file1.txt", "w") as f1, open("file2.txt", "w") as f2:
        f1.write("content1")
        f2.write("content2")
    
    # Async context managers
    class AsyncContextManager:
        async def __aenter__(self):
            return self
        
        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

# ===== 9. New built-in functions and methods =====
def builtin_examples():
    # itertools improvements
    from itertools import batched  # New in 3.12
    
    data = range(10)
    batches = list(batched(data, 3))  # [(0, 1, 2), (3, 4, 5), (6, 7, 8), (9,)]
    
    # pathlib improvements
    from pathlib import Path
    p = Path("example.txt")
    
    # Calendar improvements
    import calendar
    
    return batches

# ===== 10. Type system improvements =====
def typing_examples():
    from typing import TypedDict, Protocol, Union, Optional
    
    # TypedDict improvements
    class PersonDict(TypedDict):
        name: str
        age: int
        email: Optional[str]
    
    # Protocol improvements
    class Drawable(Protocol):
        def draw(self) -> None: ...
    
    # Union syntax improvements (| operator)
    def process_value(value: str | int | None) -> str:
        if value is None:
            return "None"
        return str(value)
    
    return PersonDict, Drawable, process_value

# ===== 11. Dataclasses improvements =====
def dataclass_examples():
    from dataclasses import dataclass, field
    
    @dataclass
    class Point:
        x: float
        y: float
        metadata: dict = field(default_factory=dict)
    
    @dataclass(slots=True)  # Performance improvement
    class FastPoint:
        x: float
        y: float
    
    return Point, FastPoint

# ===== 12. Exception groups (PEP 654) =====
def exception_group_examples():
    try:
        raise ExceptionGroup("Multiple errors", [
            ValueError("Bad value"),
            TypeError("Wrong type"),
            RuntimeError("Runtime issue")
        ])
    except* ValueError as eg:
        print(f"Caught ValueError group: {eg}")
    except* TypeError as eg:
        print(f"Caught TypeError group: {eg}")

# ===== Main test function =====
def test_python312_features():
    print("=== Python 3.12 Compatibility Test ===\n")
    
    # Test type parameters
    vector_type = Vector[int]
    matrix_type = Matrix[str, int]
    
    # Test generic functions
    result1 = process(42)
    result2 = combine("hello", 123)
    
    # Test generic classes
    container = Container(42)
    value = container.get()
    
    # Test pattern matching
    cases = [
        150,
        "this is a long string",
        [5, 5],
        {"key": "value", "extra": "data"},
        3.14
    ]
    
    for case in cases:
        result = analyze_data(case)
        print(f"analyze_data({case}) = {result}")
    
    # Test walrus operator
    walrus_examples()
    
    # Test f-strings
    fstring_results = fstring_examples()
    print(f"f-string results: {fstring_results}")
    
    # Test performance features
    perf_results = performance_examples()
    print(f"Performance test results: {perf_results}")
    
    # Test built-in improvements
    builtin_results = builtin_examples()
    print(f"Built-in test results: {builtin_results}")
    
    # Test typing improvements
    typing_results = typing_examples()
    print(f"Typing test results: {typing_results}")
    
    # Test dataclasses
    dataclass_results = dataclass_examples()
    print(f"Dataclass test results: {dataclass_results}")
    
    print("\n=== Python 3.12 Test Complete ===")

if __name__ == "__main__":
    test_python312_features()