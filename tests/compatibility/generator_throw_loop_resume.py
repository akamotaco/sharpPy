# Existing gap: after handling throw(), continue to the next loop yield.
def sequence():
    for i in range(3):
        try:
            yield i
        except ValueError:
            pass

g = sequence()
assert next(g) == 0
assert g.throw(ValueError('injected')) == 1
assert next(g) == 2
print('generator throw loop resume: PASS')

# Multiple injections must leave both nested loop iterators intact.
def nested():
    for i in range(2):
        for j in range(3):
            try:
                yield (i, j)
            except ValueError:
                pass

g = nested()
assert next(g) == (0, 0)
assert g.throw(ValueError()) == (0, 1)
assert g.throw(ValueError()) == (0, 2)
assert next(g) == (1, 0)
assert g.throw(ValueError()) == (1, 1)
assert next(g) == (1, 2)
try:
    next(g)
    assert False
except StopIteration:
    pass

events = []
def handling():
    try:
        for i in range(3):
            try:
                received = yield i
                events.append(received)
            except ValueError as error:
                events.append(str(error))
    finally:
        events.append('closed')

g = handling()
assert next(g) == 0
assert g.throw(ValueError('caught')) == 1
assert g.send(5) == 2
g.close()
assert events == ['caught', 5, 'closed']
print('generator throw nested/send/close: PASS')
