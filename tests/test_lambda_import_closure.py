# 버그 재현: 함수 내부 `import X as alias` + lambda 클로저 캡처 실패
#
# CPython 3.12: 정상 동작
# SharpPy: NameError: name 'rand_mod' is not defined
#
# 원인 추정: `import X as Y` 로 생성된 로컬 변수를 lambda가 free variable로
# 캡처하지 못함. 반면 module-level import는 globals 통해 접근 가능.

print("=== Test 1: lambda captures local variable (control) ===")

def wrap_local():
    x = 42
    return lambda: x

f1 = wrap_local()
r1 = f1()
print(f"  local capture: {r1}")
assert r1 == 42


print("=== Test 2: lambda captures 'import X as alias' inside function ===")

def wrap_import():
    import random as rand_mod
    return lambda: rand_mod.randint(1, 1)

f2 = wrap_import()
r2 = f2()
print(f"  import-alias capture: {r2}")
assert r2 == 1


print("=== Test 3: lambda captures `def`-imported name indirectly ===")

def wrap_assign():
    import random
    r = random
    return lambda: r.randint(2, 2)

f3 = wrap_assign()
r3 = f3()
print(f"  assign-after-import capture: {r3}")
assert r3 == 2


print("=== Test 4: nested def instead of lambda ===")

def wrap_def():
    import random as rand_mod
    def inner():
        return rand_mod.randint(3, 3)
    return inner

f4 = wrap_def()
r4 = f4()
print(f"  def capture: {r4}")
assert r4 == 3


print("ALL TESTS PASSED")
