# 간단한 Python 3.12 호환성 테스트

# 1. 기본 타입 지원
x = 42
y = 3.14
name = "Python"
is_active = True

# 2. 기본 연산자
result1 = x + 10
result2 = y * 2
result3 = name + " 3.12"

# 3. Walrus 연산자
if (length := len(name)) > 5:
    print(f"Name length: {length}")

# 4. 기본 컬렉션
numbers = [1, 2, 3, 4, 5]
info = {"name": "Python", "version": 3.12}
unique_nums = {1, 2, 3, 3, 4}

# 5. Type alias (Python 3.12)
type StringList = list[str]
type IntDict = dict[str, int]

# 6. 기본 함수
def greet(name: str) -> str:
    return f"Hello, {name}!"

# 7. 제네릭 함수 (Python 3.12)
def identity[T](value: T) -> T:
    return value

# 8. 제네릭 클래스 (Python 3.12) 
class Box[T]:
    def __init__(self, value: T):
        self.value = value

# 9. Match statement (기본)
def classify_number(n):
    match n:
        case 0:
            return "zero"
        case 1:
            return "one"
        case _:
            return "other"

# 10. Async function (기본)
async def async_hello():
    return "Hello async!"

print("Python 3.12 compatibility test completed")