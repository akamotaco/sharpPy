# 포괄적인 Python 3.12 호환성 테스트

# === PEP 695: Type Parameters ===
# 1. Type aliases with type parameters
type Vector[T] = list[T]
type Matrix[T] = list[list[T]]
type Callable[T, U] = T -> U

# 2. Generic functions
def identity[T](x: T) -> T:
    return x

def first[T](items: list[T]) -> T:
    return items[0]

def combine[T, U](a: T, b: U) -> tuple[T, U]:
    return (a, b)

# 3. Generic classes
class Container[T]:
    def __init__(self, value: T):
        self.value = value
    
    def get(self) -> T:
        return self.value

class Pair[T, U]:
    def __init__(self, first: T, second: U):
        self.first = first
        self.second = second

# === Match Statement (PEP 634) ===
def match_example(value):
    match value:
        case 0:
            return "zero"
        case 1 | 2 | 3:
            return "small number"
        case int() if value > 10:
            return "big number"
        case str() if len(value) > 5:
            return "long string"
        case [x, y]:
            return f"two items: {x}, {y}"
        case {"name": name, "age": age}:
            return f"person: {name}, {age}"
        case _:
            return "unknown"

# === Walrus Operator (PEP 572) ===
def walrus_examples():
    # In if statements
    if (n := len("hello")) > 3:
        print(f"Length is {n}")
    
    # In while loops
    data = [1, 2, 3]
    while (item := data.pop() if data else None) is not None:
        print(item)
    
    # In list comprehensions
    numbers = [1, 2, 3, 4, 5]
    squares = [(x, x_squared) for x in numbers if (x_squared := x**2) > 9]
    return squares

# === f-strings improvements ===
def fstring_examples():
    name = "Python"
    version = 3.12
    
    # Basic f-strings
    basic = f"Hello {name} {version}!"
    
    # f-strings with expressions
    expr = f"2 + 2 = {2 + 2}"
    
    # f-strings with format specs
    pi = 3.14159
    formatted = f"Pi: {pi:.2f}"
    
    # Nested f-strings
    nested = f"Result: {f'Value: {42}'}"
    
    return [basic, expr, formatted, nested]

# === Comprehensions ===
def comprehension_examples():
    # List comprehensions
    squares = [x**2 for x in range(5)]
    
    # Dict comprehensions
    square_dict = {x: x**2 for x in range(5)}
    
    # Set comprehensions
    even_squares = {x**2 for x in range(10) if x % 2 == 0}
    
    # Generator expressions
    sum_squares = sum(x**2 for x in range(100))
    
    # Nested comprehensions
    matrix = [[i*j for j in range(3)] for i in range(3)]
    
    return squares, square_dict, even_squares, sum_squares, matrix

# === Exception Groups (PEP 654) ===
def exception_group_example():
    try:
        raise ExceptionGroup("multiple errors", [
            ValueError("bad value"),
            TypeError("bad type")
        ])
    except* ValueError as eg:
        print(f"ValueError group: {eg}")
    except* TypeError as eg:
        print(f"TypeError group: {eg}")

# === Improved Error Messages ===
def error_message_test():
    try:
        x = {}
        y = x["missing_key"]  # KeyError with helpful message
    except KeyError as e:
        print(f"KeyError: {e}")
    
    try:
        result = 10 / 0  # ZeroDivisionError
    except ZeroDivisionError as e:
        print(f"ZeroDivisionError: {e}")

# === Async/Await improvements ===
async def async_examples():
    # Basic async function
    async def fetch_data():
        return "data"
    
    # Async with
    async with SomeAsyncContext() as ctx:
        await ctx.do_something()
    
    # Async for
    async for item in async_iterator():
        process(item)
    
    # Async comprehensions
    results = [await fetch(item) async for item in async_items()]
    return results

# === Dataclasses improvements ===
from dataclasses import dataclass, field

@dataclass
class Person:
    name: str
    age: int = 0
    hobbies: list[str] = field(default_factory=list)

@dataclass(frozen=True)
class Point:
    x: float
    y: float

# === Union types with | ===
def union_type_example(value: int | str | None) -> str:
    match value:
        case int():
            return f"integer: {value}"
        case str():
            return f"string: {value}"
        case None:
            return "none"

# === Positional-only and keyword-only parameters ===
def param_example(pos_only, /, normal, *, kwd_only):
    return f"pos: {pos_only}, normal: {normal}, kwd: {kwd_only}"

# === Type annotations ===
def annotated_function(
    items: list[int], 
    mapping: dict[str, float],
    callback: Callable[[int], str]
) -> tuple[list[str], dict[str, str]]:
    results = [callback(item) for item in items]
    str_mapping = {k: str(v) for k, v in mapping.items()}
    return results, str_mapping

# === Context managers ===
class CustomContext:
    def __enter__(self):
        print("Entering context")
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        print("Exiting context")
        return False

def context_manager_test():
    with CustomContext() as ctx:
        print("Inside context")

# === Decorators ===
def my_decorator(func):
    def wrapper(*args, **kwargs):
        print(f"Calling {func.__name__}")
        result = func(*args, **kwargs)
        print(f"Called {func.__name__}")
        return result
    return wrapper

@my_decorator
def decorated_function():
    return "Hello from decorated function"

# === Main test execution ===
if __name__ == "__main__":
    print("=== Python 3.12 Comprehensive Compatibility Test ===")
    
    # Test basic functionality
    print("\n1. Basic types and operations:")
    x, y = 42, 3.14
    print(f"x = {x}, y = {y}, x + y = {x + y}")
    
    # Test collections
    print("\n2. Collections:")
    numbers = [1, 2, 3, 4, 5]
    info = {"name": "Python", "version": 3.12}
    unique = {1, 2, 3, 2, 1}
    print(f"list: {numbers}, dict: {info}, set: {unique}")
    
    # Test comprehensions
    print("\n3. Comprehensions:")
    squares, square_dict, even_squares, sum_squares, matrix = comprehension_examples()
    print(f"List: {squares}")
    print(f"Dict: {square_dict}")
    print(f"Set: {even_squares}")
    print(f"Sum: {sum_squares}")
    
    # Test f-strings
    print("\n4. f-strings:")
    fstring_results = fstring_examples()
    for result in fstring_results:
        print(f"  {result}")
    
    # Test walrus operator
    print("\n5. Walrus operator:")
    walrus_results = walrus_examples()
    print(f"  Results: {walrus_results}")
    
    # Test match statement
    print("\n6. Match statement:")
    test_values = [0, 5, 15, "hello", "hi", [1, 2], {"name": "Alice", "age": 30}]
    for value in test_values:
        result = match_example(value)
        print(f"  match {value}: {result}")
    
    # Test decorators
    print("\n7. Decorators:")
    decorated_result = decorated_function()
    print(f"  Result: {decorated_result}")
    
    # Test context managers
    print("\n8. Context managers:")
    context_manager_test()
    
    print("\n=== Test completed ===")