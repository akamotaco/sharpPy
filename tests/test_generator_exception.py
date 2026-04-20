# generator + 사용자 정의 exception 조합 — romance() 스타일 패턴 재현
# "Stack is empty" 런타임 에러 재현 시도.

passed = 0
failed = 0

def check(name, cond, detail=""):
    global passed, failed
    if cond:
        print(f"PASS {name}")
        passed += 1
    else:
        print(f"FAIL {name}  {detail}")
        failed += 1


# ─────────────────────────────────────────────
# T1. generator 안에서 사용자 예외 raise, 바깥에서 built-in 으로 catch
# ─────────────────────────────────────────────
class MyLookup(LookupError):
    pass

def gen_raise():
    yield 1
    raise MyLookup("boom")

try:
    result = []
    for v in gen_raise():
        result.append(v)
    check("T1 raise never reached", False, "should have raised")
except LookupError as e:
    check("T1 caught by LookupError", result == [1])
except Exception as e:
    check("T1 caught by LookupError", False, f"wrong catch: {type(e).__name__}")


# ─────────────────────────────────────────────
# T2. generator 내부에서 try/except (사용자 클래스)
# ─────────────────────────────────────────────
def gen_try_inner():
    try:
        yield 1
        raise MyLookup("inside")
    except LookupError:
        yield "caught"
    yield 2

try:
    result2 = list(gen_try_inner())
    check("T2 inner try/except", result2 == [1, "caught", 2], f"got {result2}")
except Exception as e:
    check("T2 inner try/except", False, f"raised: {type(e).__name__}: {e}")


# ─────────────────────────────────────────────
# T3. yield from + 내부 raise
# ─────────────────────────────────────────────
def inner_gen():
    yield "a"
    raise MyLookup("inner")

def outer_gen():
    yield "start"
    yield from inner_gen()
    yield "end"

try:
    result3 = []
    for v in outer_gen():
        result3.append(v)
    check("T3 yield from raise", False, "should have raised")
except LookupError:
    check("T3 yield from raise caught", result3 == ["start", "a"])
except Exception as e:
    check("T3 yield from raise", False, f"wrong catch: {type(e).__name__}")


# ─────────────────────────────────────────────
# T4. generator 메서드 (instance method) + raise
# ─────────────────────────────────────────────
class Foo:
    def romance(self):
        yield "hello"
        raise MyLookup("meth")

f = Foo()
try:
    result4 = []
    for v in f.romance():
        result4.append(v)
    check("T4 method gen raise", False, "should have raised")
except LookupError:
    check("T4 method gen raise caught", result4 == ["hello"])
except Exception as e:
    check("T4 method gen raise", False, f"wrong catch: {type(e).__name__}")


# ─────────────────────────────────────────────
# T5. 모듈 import 경로 + sub-call 안에서 raise
# ─────────────────────────────────────────────
def deep_raise():
    raise MyLookup("deep")

def mid_call():
    deep_raise()

def gen_deep():
    yield "mid"
    mid_call()
    yield "never"

caught = False
try:
    for v in gen_deep():
        pass
except LookupError:
    caught = True
except Exception as e:
    check("T5 deep call raise", False, f"wrong type: {type(e).__name__}")
check("T5 deep call raise caught as LookupError", caught)


# ─────────────────────────────────────────────
# T6. try/except 내부에서 generator 반복 + 원본 예외 보존
# ─────────────────────────────────────────────
def gen_with_err():
    yield 1
    yield 2
    raise MyLookup("e6")

err_caught = None
try:
    for v in gen_with_err():
        pass
except LookupError as e:
    err_caught = e

check("T6 exception instance available", err_caught is not None and "e6" in str(err_caught),
      f"err={err_caught!r}")


# ─────────────────────────────────────────────
# T7. generator 안에 return + 이후 raise 가능성 (Morld 스타일)
# ─────────────────────────────────────────────
def gen_early_return():
    if True:
        yield "early"
        return
    yield "never"  # 도달 안 함
    raise MyLookup("never")

try:
    result7 = list(gen_early_return())
    check("T7 early return no raise", result7 == ["early"], f"got {result7}")
except Exception as e:
    check("T7 early return no raise", False, f"unexpected: {type(e).__name__}")


# ─────────────────────────────────────────────
# T8. print + yield + raise 혼합
# ─────────────────────────────────────────────
def gen_print_raise():
    print("[gen] step 1")
    yield 1
    print("[gen] step 2")
    raise MyLookup("mixed")

try:
    for v in gen_print_raise():
        pass
    check("T8 print+raise", False)
except LookupError:
    check("T8 print+raise caught", True)


print()
print(f"RESULT: {passed} passed / {failed} failed")
import sys
sys.exit(0 if failed == 0 else 1)
