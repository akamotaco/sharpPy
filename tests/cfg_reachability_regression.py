"""Dead-code removal must preserve scope analysis and every cleanup/resume edge."""
events = []

def constant():
    return 42
    while True:
        events.append("dead")

def variable(value):
    return value
    raise AssertionError("dead")

def exceptional():
    try:
        raise ValueError("live")
        events.append("dead")
    except ValueError as error:
        events.append(str(error))
    finally:
        events.append("outer")
    return 7
    events.append("dead")

def loop():
    total = 0
    for i in range(5):
        events.append(i)
        if i == 1:
            continue
            events.append("dead")
        if i == 3:
            break
            events.append("dead")
        total += i
    return total

def empty_generator():
    return 13
    yield "dead"

def delegated():
    try:
        result = yield from empty_generator()
        yield result
        return 17
        yield "dead"
    finally:
        events.append("generator-finally")

def suspending():
    try:
        return 19
        yield "dead"
    finally:
        yield "cleanup"
        events.append("resumed")

value = 10
def local_scope():
    return value
    value = 20

assert constant() == 42
marker = []
assert variable(marker) is marker
assert exceptional() == 7
assert loop() == 2
assert events == ["live", "outer", 0, 1, 2, 3]
for factory, expected in [(empty_generator, 13), (delegated, 17), (suspending, 19)]:
    g = factory()
    if factory is delegated:
        assert next(g) == 13
    if factory is suspending:
        assert next(g) == "cleanup"
    try:
        next(g)
        assert False
    except StopIteration as error:
        assert error.value == expected
for mode in ["throw", "close"]:
    g = suspending()
    assert next(g) == "cleanup"
    if mode == "throw":
        marker = ValueError("injected")
        try:
            g.throw(marker)
            assert False
        except ValueError as error:
            assert error is marker
    else:
        assert g.close() is None
try:
    local_scope()
    assert False
except NameError:
    pass
assert events == ["live", "outer", 0, 1, 2, 3, "generator-finally", "resumed"]
print("CFG reachability: PASS", events)

