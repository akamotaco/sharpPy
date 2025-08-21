a = str(123)

# 내장 데코레이터
class MyClass:
    @staticmethod
    def static_method(x):
        return x * 2
    
    @classmethod
    def class_method(cls):
        return cls.__name__

# 사용자 정의 데코레이터
def uppercase_decorator(func):
    def wrapper(*args, **kwargs):
        result = func(*args, **kwargs)
        if isinstance(result, str):
            return result.upper()
        return result
    return wrapper

@uppercase_decorator
def greet(name):
    return f"hello, {name}"

print(greet("world"))  # "HELLO, WORLD"
# 다중 데코레이터
@uppercase_decorator
@staticmethod
def combined_decorator(text):
    return text

# 클래스 데코레이터
def add_id(cls):
    cls.id = 100
    return cls

@add_id
class MyClass:
    pass

print(MyClass.id)  # 100