"""
Test: dict subscript operations (d[key] get/set/del)
CPython 3.12: Objects/dictobject.c:2488-2530 (dict_subscript / dict_ass_sub)
"""

# Test 1: basic get/set
print("=== Test 1: Basic Get/Set ===")
d = {}
d["a"] = 1
d["b"] = 2
assert d["a"] == 1
assert d["b"] == 2
print("Test 1 passed: basic get/set")

# Test 2: overwrite
print("\n=== Test 2: Overwrite ===")
d["a"] = 99
assert d["a"] == 99
print("Test 2 passed: overwrite")

# Test 3: KeyError
print("\n=== Test 3: KeyError ===")
try:
    _ = d["missing"]
    assert False, "Should raise KeyError"
except KeyError:
    print("Test 3 passed: KeyError on missing key")

# Test 4: various key types
print("\n=== Test 4: Various Key Types ===")
d2 = {1: "int", (1, 2): "tuple", True: "bool"}
assert d2[1] == "bool"  # True overwrites 1 (bool is subclass of int)
assert d2[(1, 2)] == "tuple"
print("Test 4 passed: various key types")

# Test 5: insertion order
print("\n=== Test 5: Insertion Order ===")
d3 = {}
d3["c"] = 3
d3["a"] = 1
d3["b"] = 2
assert list(d3.keys()) == ["c", "a", "b"]
print("Test 5 passed: insertion order preserved")

# Test 6: del
print("\n=== Test 6: Del ===")
del d3["a"]
assert "a" not in d3
assert list(d3.keys()) == ["c", "b"]
print("Test 6 passed: del preserves order")

# Test 7: __missing__ in subclass
print("\n=== Test 7: __missing__ Subclass ===")
class DefaultDict(dict):
    def __missing__(self, key):
        self[key] = 0
        return 0

dd = DefaultDict()
assert dd["x"] == 0
assert dd["x"] == 0
print("Test 7 passed: __missing__ subclass")

# Test 8: None as key
print("\n=== Test 8: None Key ===")
d4 = {None: "none_value"}
assert d4[None] == "none_value"
d4[None] = "updated"
assert d4[None] == "updated"
print("Test 8 passed: None as key")

# Test 9: integer key subscript
print("\n=== Test 9: Integer Key ===")
d5 = {0: "zero", 1: "one", -1: "neg_one"}
assert d5[0] == "zero"
assert d5[1] == "one"
assert d5[-1] == "neg_one"
print("Test 9 passed: integer key subscript")

# Test 10: nested dict subscript
print("\n=== Test 10: Nested Dict ===")
d6 = {"outer": {"inner": 42}}
assert d6["outer"]["inner"] == 42
d6["outer"]["inner"] = 99
assert d6["outer"]["inner"] == 99
print("Test 10 passed: nested dict subscript")

print("\n=== All dict subscript tests passed! ===")
