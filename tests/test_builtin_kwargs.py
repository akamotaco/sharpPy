# tests/test_builtin_kwargs.py — builtin function kwargs 회귀 테스트
#
# CPython 3.12 호환 검증:
# 1. 내장 함수(print, sorted 등)에 keyword argument 전달
# 2. Python 함수에 keyword argument 전달
# 3. positional + keyword 혼합 호출
#
# 실행: dotnet run -c release tests/test_builtin_kwargs.py

passed = 0
failed = 0

def check(name, actual, expected):
    global passed, failed
    if actual == expected:
        passed += 1
    else:
        failed += 1
        print(f"  FAIL: {name} — expected {expected!r}, got {actual!r}")


print("=== 1. Python function kwargs ===")

def greet(name, greeting="Hello"):
    return f"{greeting}, {name}!"

check("positional only", greet("World"), "Hello, World!")
check("positional + kwarg", greet("World", greeting="Hi"), "Hi, World!")
check("all kwargs", greet(name="World", greeting="Hey"), "Hey, World!")
check("kwarg only (name)", greet(name="World"), "Hello, World!")


print("\n=== 2. Python function with many defaults ===")

def make_location(region_id, loc_id, name, stay=0, indoor=True, owner=None,
                  desc=None, ground=None, geometry="line", length=0):
    return {
        "region_id": region_id, "loc_id": loc_id, "name": name,
        "stay": stay, "indoor": indoor, "owner": owner,
        "desc": desc, "ground": ground, "geometry": geometry, "length": length
    }

# positional only
result = make_location(100, 0, "입구")
check("all defaults", result["length"], 0)
check("all defaults geometry", result["geometry"], "line")

# skip middle defaults, set last kwarg
result = make_location(100, 0, "입구", length=200)
check("skip to last kwarg", result["length"], 200)
check("skip defaults preserved", result["stay"], 0)
check("skip defaults indoor", result["indoor"], True)

# mix positional and kwargs
result = make_location(100, 0, "입구", 5, False, length=300)
check("mix pos+kwarg", result["length"], 300)
check("mix stay", result["stay"], 5)
check("mix indoor", result["indoor"], False)

# multiple kwargs
result = make_location(100, 0, "입구", geometry="ring", length=150, indoor=False)
check("multi kwargs length", result["length"], 150)
check("multi kwargs geometry", result["geometry"], "ring")
check("multi kwargs indoor", result["indoor"], False)


print("\n=== 3. Built-in function kwargs ===")

# sorted with key kwarg
data = [3, 1, 4, 1, 5]
check("sorted default", sorted(data), [1, 1, 3, 4, 5])
check("sorted reverse=True", sorted(data, reverse=True), [5, 4, 3, 1, 1])

# sorted with key function
words = ["banana", "apple", "cherry"]
check("sorted key=len", sorted(words, key=len), ["apple", "banana", "cherry"])

# print with kwargs (just test it doesn't crash)
import io
# Can't easily capture print output, so just verify no crash
print("test", end="")
print(" ok", end="\n")
check("print with end kwarg", True, True)

# dict constructor
d = dict(a=1, b=2, c=3)
check("dict kwargs", d, {"a": 1, "b": 2, "c": 3})

# int with base kwarg
check("int base=16", int("ff", base=16), 255)
check("int base=2", int("1010", base=2), 10)


print("\n=== 4. Class __init__ kwargs ===")

class Point:
    def __init__(self, x=0, y=0, z=0):
        self.x = x
        self.y = y
        self.z = z

p1 = Point(1, 2, 3)
check("Point positional", (p1.x, p1.y, p1.z), (1, 2, 3))

p2 = Point(x=10, z=30)
check("Point kwargs", (p2.x, p2.y, p2.z), (10, 0, 30))

p3 = Point(1, z=5)
check("Point mix", (p3.x, p3.y, p3.z), (1, 0, 5))


print("\n=== 5. Lambda with kwargs ===")

add = lambda a, b=10: a + b
check("lambda default", add(5), 15)
check("lambda kwarg", add(5, b=20), 25)


print("\n=== 6. Unexpected kwargs → TypeError (CPython 3.12 호환) ===")

# CPython 3.12: "X() takes no keyword arguments"
def check_raises(name, fn, expected_type=TypeError):
    global passed, failed
    try:
        fn()
        failed += 1
        print(f"  FAIL: {name} — expected {expected_type.__name__} but no exception raised")
    except expected_type:
        passed += 1
    except Exception as e:
        failed += 1
        print(f"  FAIL: {name} — expected {expected_type.__name__}, got {type(e).__name__}: {e}")

# 내장 함수: kwargs를 전혀 받지 않는 함수들
check_raises("len() unexpected kwarg", lambda: len([], foo=1))
check_raises("abs() unexpected kwarg", lambda: abs(-1, foo=1))
check_raises("isinstance() unexpected kwarg", lambda: isinstance(1, int, foo=1))
check_raises("range() unexpected kwarg", lambda: range(10, step=2))

# 내장 함수: 일부 kwargs만 받는 함수 — 잘못된 kwarg
check_raises("int() unexpected kwarg", lambda: int("10", foo=1))
check_raises("sorted() unexpected kwarg", lambda: sorted([3,1,2], foo=True))
check_raises("print() unexpected kwarg", lambda: print("test", foo=1))
check_raises("max() unexpected kwarg", lambda: max([1,2,3], foo=1))

# PyType 경로: kwargs를 전혀 받지 않는 타입들
# CPython: X_vectorcall → _PyArg_NoKwnames → "X() takes no keyword arguments"
check_raises("float() unexpected kwarg", lambda: float(3.14, foo=1))
check_raises("bool() unexpected kwarg", lambda: bool(1, foo=1))
check_raises("list() unexpected kwarg", lambda: list([1], foo=1))
check_raises("tuple() unexpected kwarg", lambda: tuple([1], foo=1))
check_raises("set() unexpected kwarg", lambda: set([1], foo=1))
check_raises("frozenset() unexpected kwarg", lambda: frozenset([1], foo=1))

# PyType 경로: 일부 kwargs만 받는 타입
# CPython: _PyArg_UnpackKeywords → "'foo' is an invalid keyword argument for X()"
check_raises("str() unexpected kwarg", lambda: str("hi", foo=1))

# valid kwargs가 정상 동작하는지도 확인
check("int(base=16) valid kwarg", int("ff", base=16), 255)
check("str() no args", str(), "")
check("str(object=123)", str(object=123), "123")


print(f"\n{'='*50}")
print(f"TOTAL: {passed}/{passed+failed} passed, {failed} failed")
if failed == 0:
    print("ALL PASSED")
else:
    print("SOME TESTS FAILED")
