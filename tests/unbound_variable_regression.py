"""CPython 3.12 unbound locals/cells/freevars: types, messages, args, name."""
def check(label, callback, expected_type, name, free=False):
    try:
        callback()
        raise AssertionError("missing error: " + label)
    except NameError as error:
        assert type(error) is expected_type
        expected = ("cannot access free variable '" + name +
                    "' where it is not associated with a value in enclosing scope") if free else (
                    "cannot access local variable '" + name +
                    "' where it is not associated with a value")
        assert str(error) == expected
        assert error.args == (expected,)
        assert error.name == (name if free else None)
        print(label, type(error).__name__, str(error), error.name)

value = "global"
def missing():
    return value
    value = 1

def deleted(value):
    del value
    return value

def delete_twice(value):
    del value
    del value

def delete_missing():
    del value

check("local", missing, UnboundLocalError, "value")
check("deleted-local", lambda: deleted(None), UnboundLocalError, "value")
check("delete-twice", lambda: delete_twice(1), UnboundLocalError, "value")
check("delete-missing", delete_missing, UnboundLocalError, "value")
assert value == "global"

def captured_local():
    def read():
        return value
    return value
    value = 1

def captured_none(value):
    def read():
        return value
    return read()
assert captured_none(None) is None

def captured_parameter(value):
    def read():
        return value
    del value
    return value

def delete_cell_twice(value):
    def read():
        return value
    del value
    del value

def delete_only_cell():
    def read():
        return value
    del value

check("delete-only-cell", delete_only_cell, UnboundLocalError, "value")
check("local-cell", captured_local, UnboundLocalError, "value")
check("parameter-cell", lambda: captured_parameter(None), UnboundLocalError, "value")
check("delete-cell-twice", lambda: delete_cell_twice(1), UnboundLocalError, "value")

def free_functions():
    def read():
        return value
    def erase():
        nonlocal value
        del value
    def set_value(new):
        nonlocal value
        value = new
    return read, erase, set_value
    value = 1

read, erase, set_value = free_functions()
check("free-read", read, NameError, "value", True)
check("free-delete", erase, NameError, "value", True)
set_value(None)
assert read() is None
set_value(17)
assert read() == 17
erase()
check("free-deleted", read, NameError, "value", True)

# Mix a captured parameter, local cell, and enclosing free variable to exercise
# SharpPy's free-first cell storage versus localsplus instruction operands.
def mixed_outer():
    outer = 2
    def middle(parameter):
        local = 3
        def inner():
            nonlocal parameter, local, outer
            parameter += 1
            local += 1
            outer += 1
            return parameter, local, outer
        assert inner() == (5, 4, 3)
        assert inner() == (6, 5, 4)
        return parameter, local, outer
    return middle(4)
assert mixed_outer() == (6, 5, 4)

def mixed_store_delete():
    alpha = 1
    zeta = 2
    def middle():
        nonlocal alpha, zeta
        local = 3
        def read_local():
            return local
        alpha = 11
        zeta = 12
        del alpha
        assert read_local() == 3
        return zeta
    assert middle() == 12
    return alpha
check("mixed-store-delete", mixed_store_delete, UnboundLocalError, "alpha")

# Exception targets are cleared after leaving except, including captured ones.
def exception_target():
    try:
        raise ValueError("handled")
    except ValueError as error:
        pass
    return error
check("except-target", exception_target, UnboundLocalError, "error")

for error in [NameError("manual"), UnboundLocalError("manual")]:
    assert error.name is None
    error.name = 42
    assert error.name == 42
    del error.name
    assert error.name is None

# LOAD_FAST_AND_CLEAR in an inlined comprehension can save an unbound value.
def comprehension():
    assert [value for value in range(3)] == [0, 1, 2]
    return value
    value = 1
check("comprehension-restore", comprehension, UnboundLocalError, "value")
print("unbound variable regression: PASS")
