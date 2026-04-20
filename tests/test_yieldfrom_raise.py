# yield from + raise 버그 분리

# Case A: built-in exception
def inner_a():
    yield "x"
    raise ValueError("boom-a")

def outer_a():
    yield "pre"
    yield from inner_a()
    yield "post"

print("=== A: built-in ValueError ===")
try:
    result = list(outer_a())
    print(f"  NO ERR: {result}")
except ValueError as e:
    print(f"  OK caught ValueError: {e}")
except Exception as e:
    print(f"  WRONG: {type(e).__name__}: {e}")

# Case B: user-defined custom exception
class MyErr(Exception):
    pass

def inner_b():
    yield "x"
    raise MyErr("boom-b")

def outer_b():
    yield "pre"
    yield from inner_b()
    yield "post"

print("=== B: user-defined MyErr(Exception) ===")
try:
    result = list(outer_b())
    print(f"  NO ERR: {result}")
except MyErr as e:
    print(f"  OK caught MyErr: {e}")
except Exception as e:
    print(f"  WRONG: {type(e).__name__}: {e}")

# Case C: inner 가 yield 없이 바로 raise
def inner_c():
    raise ValueError("boom-c")
    yield  # unreachable but makes it a generator

def outer_c():
    yield "pre"
    yield from inner_c()
    yield "post"

print("=== C: inner raises before any yield ===")
try:
    result = list(outer_c())
    print(f"  NO ERR: {result}")
except ValueError as e:
    print(f"  OK caught: {e}")
except Exception as e:
    print(f"  WRONG: {type(e).__name__}: {e}")

# Case D: outer 안에 try/except 로 감싸는 경우
def outer_d():
    yield "pre"
    try:
        yield from inner_a()
    except ValueError:
        yield "caught"
    yield "post"

print("=== D: outer catches inner's ValueError ===")
try:
    result = list(outer_d())
    print(f"  OK: {result}")
except Exception as e:
    print(f"  WRONG: {type(e).__name__}: {e}")
