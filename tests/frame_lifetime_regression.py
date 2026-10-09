"""Live frames and suspended generators must retain independent state."""
import sys


def capture(value):
    return sys._getframe()


first = capture(1)
second = capture(2)
assert first is not second
assert first.f_code.co_name == "capture"
assert second.f_code.co_name == "capture"


def empty():
    return None


for _ in range(10):
    assert empty() is None
assert first.f_code.co_name == "capture"


def values(initial):
    current = initial
    yield current
    current += 1
    yield current


left = values(1)
right = values(10)
assert next(left) == 1
assert next(right) == 10
assert next(left) == 2
assert next(right) == 11


def handled(value):
    try:
        return 10 // value
    except ZeroDivisionError:
        return -1


assert handled(2) == 5
assert handled(0) == -1
print("frame lifetime regression: PASS")
