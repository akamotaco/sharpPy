"""Bound and unbound builtin descriptors keep receiver, arguments and errors."""
values = []
append = values.append
append(1)
list.append(values, 2)
values.extend([3, 4])
assert values == [1, 2, 3, 4]
assert values.pop() == 4
assert list.pop(values, 0) == 1
assert values == [2, 3]
assert values.count(3) == 1
assert list.count(values, 99) == 0

data = {"a": 1}
get = data.get
assert get("a") == 1
assert get("missing", 9) == 9
assert dict.get(data, "missing", 7) == 7
assert data.pop("a") == 1
assert dict.pop(data, "missing", 5) == 5

values[0] = 20
assert values[0] == 20
del values[1]
assert values == [20]
data["x"] = 3
assert data["x"] == 3
del data["x"]
assert data == {}
assert (4, 5)[1] == 5
assert "ab"[1] == "b"

assert "hello".upper() == "HELLO"
assert str.upper("world") == "WORLD"
assert (1, 2, 1).count(1) == 2
assert tuple.count((1, 2), 3) == 0

try:
    append()
except TypeError:
    pass
else:
    raise AssertionError("missing builtin argument did not raise TypeError")

try:
    values.pop(100)
except IndexError:
    pass
else:
    raise AssertionError("builtin error did not propagate")
print("builtin descriptor regression: PASS")
