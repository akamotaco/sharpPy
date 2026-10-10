"""Finally scope lifetime, control transfers, and exception-state restoration."""
events = []

def sequential():
    for i in range(2):
        try:
            events.append(("body", i))
        finally:
            events.append(("finally", i))
    try:
        events.append("second")
    finally:
        events.append("second-finally")
    return 23

assert sequential() == 23
assert events == [("body", 0), ("finally", 0), ("body", 1), ("finally", 1),
                  "second", "second-finally"]

def override(mode, dynamic):
    try:
        raise ValueError("original")
    finally:
        if mode == "constant":
            return 7
        if mode == "dynamic":
            return dynamic
        if mode == "raise":
            raise TypeError("replacement")

assert override("constant", None) == 7
marker = []
assert override("dynamic", marker) is marker
try:
    override("raise", None)
    assert False
except TypeError as error:
    assert str(error) == "replacement"
    assert isinstance(error.__context__, ValueError)
try:
    override("propagate", None)
    assert False
except ValueError as error:
    assert str(error) == "original"

# An exception in inlined finally must bypass the try's own except clause.
def return_cleanup_error(value):
    try:
        return value
    except ValueError:
        events.append("wrong-handler")
    finally:
        raise ValueError("cleanup")

events.clear()
try:
    return_cleanup_error(marker)
    assert False
except ValueError as error:
    assert str(error) == "cleanup"
assert events == []

# Finally can override a return, including its preserved nonconstant value.
def loop_override(mode):
    for i in range(3):
        try:
            return i + 10
        finally:
            events.append(i)
            if mode == "continue":
                continue
            if mode == "break":
                break
    return 99

for mode, expected in [("continue", [0, 1, 2]), ("break", [0])]:
    events.clear()
    assert loop_override(mode) == 99
    assert events == expected

# Break/continue from exceptional finally discard the pending exception and
# restore the outer handled exception before a subsequent bare raise.
def exception_loop(mode):
    try:
        raise LookupError("outer")
    except LookupError as outer:
        for i in range(3):
            try:
                raise ValueError("inner")
            finally:
                events.append(i)
                if mode == "break":
                    break
                continue
        try:
            raise
        except LookupError as restored:
            assert restored is outer
        return 31

for mode, expected in [("continue", [0, 1, 2]), ("break", [0])]:
    events.clear()
    assert exception_loop(mode) == 31
    assert events == expected

# Normal and exceptional finally copies may suspend; neither may repeat.
def suspended(fail):
    try:
        if fail:
            raise ValueError("pending")
        return 41
    finally:
        events.append("enter")
        yield "cleanup"
        events.append("leave")

for fail in [False, True]:
    events.clear()
    g = suspended(fail)
    assert next(g) == "cleanup"
    try:
        next(g)
        assert False
    except StopIteration as error:
        assert not fail and error.value == 41
    except ValueError as error:
        assert fail and str(error) == "pending"
    assert events == ["enter", "leave"]

for mode in ["throw", "close"]:
    events.clear()
    g = suspended(True)
    assert next(g) == "cleanup"
    if mode == "throw":
        injected = TypeError("injected")
        try:
            g.throw(injected)
            assert False
        except TypeError as error:
            assert error is injected
    else:
        assert g.close() is None
    assert events == ["enter"]

print("finally scope lifetime: PASS")
