# SharpPy Python 3.12 호환성 테스트 프로그램

# 테스트할 수 있는 기본 기능들
def test_basic_functionality():
    print("=== 기본 기능 테스트 ===")
    
    # 기본 타입
    x = 42
    y = 3.14
    name = "Python"
    is_active = True
    
    print(f"정수: {x}")
    print(f"실수: {y}")
    print(f"문자열: {name}")
    print(f"불린: {is_active}")
    
    # 기본 연산
    result = x + 10
    print(f"연산 결과: {result}")
    
    # 컬렉션
    numbers = [1, 2, 3, 4, 5]
    info = {"name": "SharpPy", "version": "0.1"}
    unique_nums = {1, 2, 3, 4, 5}
    
    print(f"리스트: {numbers}")
    print(f"딕셔너리: {info}")
    print(f"집합: {unique_nums}")

def test_functions():
    print("\n=== 함수 테스트 ===")
    
    def greet(name):
        return f"Hello, {name}!"
    
    result = greet("World")
    print(f"함수 호출 결과: {result}")

def test_classes():
    print("\n=== 클래스 테스트 ===")
    
    class Person:
        def __init__(self, name, age):
            self.name = name
            self.age = age
        
        def introduce(self):
            return f"My name is {self.name} and I'm {self.age} years old."
    
    person = Person("Alice", 30)
    print(person.introduce())

def test_control_flow():
    print("\n=== 제어 구조 테스트 ===")
    
    # if 문
    x = 10
    if x > 5:
        print("x is greater than 5")
    
    # for 문
    for i in range(3):
        print(f"Loop iteration: {i}")
    
    # while 문
    count = 0
    while count < 3:
        print(f"Count: {count}")
        count += 1

def test_exceptions():
    print("\n=== 예외 처리 테스트 ===")
    
    try:
        result = 10 / 0
    except ZeroDivisionError:
        print("Caught division by zero error")

# 메인 테스트 실행
if __name__ == "__main__":
    print("🐍 SharpPy Python 3.12 호환성 테스트")
    print("=====================================")
    
    test_basic_functionality()
    test_functions()
    test_classes()
    test_control_flow()
    test_exceptions()
    
    print("\n✅ 테스트 완료!")