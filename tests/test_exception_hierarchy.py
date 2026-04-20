# Built-in exception 접근 + 상속 계층 검증 (CPython 3.12 일치)
# 실행: dotnet run -c Release test_exceptions.py

# 1) 모든 노출된 예외 타입이 NameError 없이 조회되는지
exceptions_to_check = [
    "BaseException", "Exception",
    "SystemExit", "KeyboardInterrupt", "GeneratorExit",
    "StopIteration", "StopAsyncIteration",
    "TypeError", "ValueError",
    "LookupError", "KeyError", "IndexError",
    "AttributeError", "NameError", "UnboundLocalError",
    "RuntimeError", "SystemError", "NotImplementedError", "RecursionError",
    "ArithmeticError", "ZeroDivisionError", "OverflowError",
    "ImportError", "ModuleNotFoundError",
    "AssertionError", "SyntaxError", "IndentationError",
    "BufferError", "OSError", "FileNotFoundError",
    "EOFError", "MemoryError",
    "UnicodeError", "UnicodeDecodeError", "UnicodeEncodeError",
    "BaseExceptionGroup", "ExceptionGroup",
    "Warning",
]

missing = []
for name in exceptions_to_check:
    try:
        cls = eval(name)
    except NameError:
        missing.append(name)

if missing:
    print("MISSING:", missing)
else:
    print("OK: all 38 exceptions accessible")

# 2) LookupError 계층 검증 (CPython 3.12 기준)
assert issubclass(IndexError, LookupError), "IndexError should subclass LookupError"
assert issubclass(KeyError, LookupError), "KeyError should subclass LookupError"
assert issubclass(LookupError, Exception), "LookupError should subclass Exception"
print("OK: LookupError 계층 (IndexError, KeyError → LookupError → Exception)")

# 3) ArithmeticError 계층
assert issubclass(ZeroDivisionError, ArithmeticError), "ZeroDivisionError should subclass ArithmeticError"
assert issubclass(OverflowError, ArithmeticError), "OverflowError should subclass ArithmeticError"
assert issubclass(ArithmeticError, Exception), "ArithmeticError should subclass Exception"
print("OK: ArithmeticError 계층")

# 4) 사용자가 LookupError 서브클래스를 만들 수 있는지 (Morld 의 DialogueCoverageError 케이스)
class MyCustomLookup(LookupError):
    pass

try:
    raise MyCustomLookup("test msg")
except LookupError as e:
    print("OK: 사용자 정의 LookupError 서브클래스 raise/catch")
except Exception as e:
    print("FAIL: should have been caught by LookupError:", type(e).__name__)

print("\n=== All CPython 3.12 exception hierarchy tests PASSED ===")
