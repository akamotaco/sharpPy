# 사용자 정의 서브클래스의 except 매칭 분석

class MyCustomLookup(LookupError):
    pass

# issubclass 검증 (이미 통과함을 확인)
print("issubclass(MyCustomLookup, LookupError):", issubclass(MyCustomLookup, LookupError))
print("issubclass(MyCustomLookup, Exception):", issubclass(MyCustomLookup, Exception))

# built-in 서브클래스는 except LookupError 로 잡히는가?
try:
    raise IndexError("builtin")
except LookupError as e:
    print("OK: built-in IndexError caught by except LookupError")
except Exception as e:
    print("FAIL: built-in IndexError NOT caught by LookupError")

# 사용자 정의 서브클래스는?
try:
    raise MyCustomLookup("custom")
except LookupError as e:
    print("OK: user MyCustomLookup caught by except LookupError")
except Exception as e:
    print(f"FAIL: user MyCustomLookup NOT caught by LookupError, caught by Exception as {type(e).__name__}")

# 사용자 정의 Exception 직계는?
class MyException(Exception):
    pass
try:
    raise MyException("x")
except Exception as e:
    print("OK: user Exception subclass caught by except Exception")

# 사용자 정의 ValueError 서브?
class MyValueError(ValueError):
    pass
try:
    raise MyValueError("x")
except ValueError as e:
    print("OK: user ValueError subclass caught by except ValueError")
except Exception as e:
    print("FAIL: user ValueError subclass NOT caught by ValueError")
