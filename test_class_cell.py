# __class__ cell 회귀 테스트 — CPython 3.12 과 동일 동작 검증
# (symtable.c: 함수 내 'super' 이름 Load 또는 '__class__' Load → class cell 연결)

passed = 0
failed = 0

def check(name, cond):
    global passed, failed
    if cond:
        passed += 1
    else:
        failed += 1
        print("FAIL:", name)

# 1. bare __class__ 참조
class A:
    def who(self):
        return __class__

check("bare __class__", A().who() is A)

# 2. super 이름만 로드 후 별칭으로 zero-arg 호출
class Base:
    def compute(self, x):
        return x + 1

class B(Base):
    def compute(self, x):
        s = super
        return s().compute(x) * 2

check("super alias zero-arg", B().compute(3) == 8)

# 3. 기존 super() 직접 호출 (회귀 확인)
class C(Base):
    def compute(self, x):
        return super().compute(x) * 3

check("direct super()", C().compute(3) == 12)

# 4. 2-arg super (명시적) — __class__ cell 불필요하지만 동작해야 함
class D(Base):
    def compute(self, x):
        return super(D, self).compute(x) * 4

check("explicit 2-arg super", D().compute(3) == 16)

# 5. 중첩 함수에서 __class__ 참조
class E:
    def who(self):
        def inner():
            return __class__
        return inner()

check("nested __class__", E().who() is E)

# 6. __class__ 와 self.__class__ 구분 (상속 시 정적 vs 동적)
class F:
    def static_cls(self):
        return __class__
    def dynamic_cls(self):
        return self.__class__

class G(F):
    pass

g = G()
check("__class__ is defining class", g.static_cls() is F)
check("self.__class__ is runtime class", g.dynamic_cls() is G)

# 7. __class__ 를 조건식/f-string 등 값 위치에서 사용
class H:
    def name_of(self):
        return f"{__class__.__name__}"

check("__class__ in f-string", H().name_of() == "H")

print(f"TOTAL: {passed} passed, {failed} failed")
if failed == 0:
    print("ALL PASSED")
