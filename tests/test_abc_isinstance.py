# tests/test_abc_isinstance.py — ABC isinstance 회귀 테스트
#
# CPython 3.12 호환 검증:
# 1. 내장타입 dunder method 노출 (hasattr)
# 2. ABCMeta metaclass + __instancecheck__ + __subclasshook__ 체인
# 3. random.sample(list) 동작 (Sequence ABC 판정 의존)
#
# 실행: dotnet run -c release tests/test_abc_isinstance.py

import abc
import random

passed = 0
failed = 0

def check(name, actual, expected):
    global passed, failed
    if actual == expected:
        passed += 1
    else:
        failed += 1
        print(f"  FAIL: {name} — expected {expected}, got {actual}")


print("=== 1. Built-in type dunder exposure ===")
# CPython 3.12 기준: list/str/tuple/range/dict의 dunder 노출 여부

# list: 5/5
check("list.__getitem__", hasattr(list, '__getitem__'), True)
check("list.__len__", hasattr(list, '__len__'), True)
check("list.__contains__", hasattr(list, '__contains__'), True)
check("list.__iter__", hasattr(list, '__iter__'), True)
check("list.__reversed__", hasattr(list, '__reversed__'), True)

# str: 4/5 (no __reversed__)
check("str.__getitem__", hasattr(str, '__getitem__'), True)
check("str.__len__", hasattr(str, '__len__'), True)
check("str.__contains__", hasattr(str, '__contains__'), True)
check("str.__iter__", hasattr(str, '__iter__'), True)

# tuple: 4/5 (no __reversed__)
check("tuple.__getitem__", hasattr(tuple, '__getitem__'), True)
check("tuple.__len__", hasattr(tuple, '__len__'), True)
check("tuple.__contains__", hasattr(tuple, '__contains__'), True)
check("tuple.__iter__", hasattr(tuple, '__iter__'), True)

# range: 5/5
check("range.__getitem__", hasattr(range, '__getitem__'), True)
check("range.__len__", hasattr(range, '__len__'), True)
check("range.__contains__", hasattr(range, '__contains__'), True)
check("range.__iter__", hasattr(range, '__iter__'), True)
check("range.__reversed__", hasattr(range, '__reversed__'), True)

# dict: 5/5
check("dict.__getitem__", hasattr(dict, '__getitem__'), True)
check("dict.__len__", hasattr(dict, '__len__'), True)
check("dict.__contains__", hasattr(dict, '__contains__'), True)
check("dict.__iter__", hasattr(dict, '__iter__'), True)
check("dict.__reversed__", hasattr(dict, '__reversed__'), True)

# range is type (not function)
check("type(range)", type(range), type)


print("\n=== 2. ABCMeta metaclass chain ===")

# ABCMeta로 직접 만든 클래스
class HasLen(metaclass=abc.ABCMeta):
    @classmethod
    def __subclasshook__(cls, C):
        if cls is HasLen:
            if hasattr(C, '__len__'):
                return True
        return NotImplemented

check("type(HasLen) is ABCMeta", type(HasLen) is abc.ABCMeta, True)
check("isinstance([], HasLen)", isinstance([], HasLen), True)
check("isinstance('abc', HasLen)", isinstance('abc', HasLen), True)
check("isinstance(123, HasLen)", isinstance(123, HasLen), False)

# __subclasshook__ with multiple methods
class HasGetitemLen(metaclass=abc.ABCMeta):
    @classmethod
    def __subclasshook__(cls, C):
        if cls is HasGetitemLen:
            if hasattr(C, '__getitem__') and hasattr(C, '__len__'):
                return True
        return NotImplemented

check("isinstance([], HasGetitemLen)", isinstance([], HasGetitemLen), True)
check("isinstance({}, HasGetitemLen)", isinstance({}, HasGetitemLen), True)
check("isinstance(123, HasGetitemLen)", isinstance(123, HasGetitemLen), False)

# register() 동작
class MyABC(metaclass=abc.ABCMeta):
    pass

MyABC.register(int)
check("isinstance(42, MyABC) after register", isinstance(42, MyABC), True)
check("isinstance('x', MyABC) no register", isinstance('x', MyABC), False)


print("\n=== 3. random.sample (Sequence ABC dependency) ===")

# 기본 list
result = random.sample([1, 2, 3, 4, 5], 3)
check("random.sample(list, 3) length", len(result), 3)

# 클래스 변수 list
class MyClass:
    ITEMS = ["a", "b", "c", "d", "e"]
    def do_sample(self):
        return random.sample(self.ITEMS, 3)

obj = MyClass()
try:
    result2 = obj.do_sample()
    check("random.sample(class_var_list, 3)", len(result2), 3)
except TypeError as e:
    check("random.sample(class_var_list, 3)", str(e), "SHOULD NOT FAIL")

# 상속된 클래스 변수
class Parent:
    POOL = ["x", "y", "z", "w", "v"]

class Child(Parent):
    def sample(self):
        return random.sample(self.POOL, 2)

try:
    result3 = Child().sample()
    check("random.sample(inherited_class_var, 2)", len(result3), 2)
except TypeError as e:
    check("random.sample(inherited_class_var, 2)", str(e), "SHOULD NOT FAIL")


print("\n=== 4. range() as type ===")

r = range(10)
check("range(10)[3]", r[3], 3)
check("len(range(10))", len(r), 10)
check("5 in range(10)", 5 in r, True)
check("15 in range(10)", 15 in r, False)
check("list(range(3))", list(range(3)), [0, 1, 2])
check("list(range(1,4))", list(range(1, 4)), [1, 2, 3])
check("list(range(0,10,3))", list(range(0, 10, 3)), [0, 3, 6, 9])


print(f"\n{'='*50}")
print(f"TOTAL: {passed}/{passed+failed} passed, {failed} failed")
if failed == 0:
    print("ALL PASSED")
else:
    print("SOME TESTS FAILED")
