# test_str_join.py - str.join iterable 지원 테스트

# 기본: list
assert ",".join(["a", "b", "c"]) == "a,b,c"

# 기본: tuple
assert ",".join(("x", "y")) == "x,y"

# 빈 iterable
assert ",".join([]) == ""
assert ",".join(()) == ""

# generator expression
assert ",".join(x for x in ["a", "b", "c"]) == "a,b,c"

# generator with filter
assert ",".join(x for x in ["a", "", "b", "", "c"] if x) == "a,b,c"

# generator function
def gen():
    yield "hello"
    yield "world"

assert " ".join(gen()) == "hello world"

# set (순서 불확정이므로 길이/구분자만 확인)
result = ",".join({"a"})
assert result == "a"

# dict keys
result = " ".join({"k1": 1, "k2": 2})
assert "k1" in result
assert "k2" in result

# 중첩 generator
assert "-".join(s.upper() for s in ["a", "b"]) == "A-B"

# 에러: non-str 요소
try:
    ",".join([1, 2])
    assert False, "should raise TypeError"
except TypeError:
    pass

# 에러: non-str in generator
try:
    ",".join(x for x in [1, "a"])
    assert False, "should raise TypeError"
except TypeError:
    pass

print("All str.join tests passed!")
