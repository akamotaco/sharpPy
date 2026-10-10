events = []
class Manager:
    def __enter__(self):
        events.append('enter')
        return self
    def __exit__(self, kind, value, tb):
        events.append('exit')
        assert kind is None
        return False

def constant(flag):
    for i in range(3):
        with Manager():
            try:
                if flag:
                    return 42
                return None
            finally:
                events.append('finally')
assert constant(True) == 42
assert constant(False) is None
assert events == ['enter', 'finally', 'exit', 'enter', 'finally', 'exit']

def overridden():
    try:
        return 42
    finally:
        return 9
assert overridden() == 9

failure = ValueError('cleanup')
class RaisingManager(Manager):
    def __exit__(self, kind, value, tb):
        raise failure
def failed():
    with RaisingManager():
        return 42
try:
    failed()
except ValueError as exc:
    assert exc is failure
else:
    assert False

def expression():
    try:
        events.append('value')
        return [1]
    finally:
        events.append('expression cleanup')
assert expression() == [1]
assert events[-2:] == ['value', 'expression cleanup']

def suspending():
    try:
        return 42
    finally:
        yield 'cleanup'
g = suspending()
assert next(g) == 'cleanup'
try:
    next(g)
except StopIteration as exc:
    assert exc.value == 42
else:
    assert False

def delegated(flag):
    yield from [1, 2]
    if flag:
        return 3
    return 4
for flag in [True, False]:
    g = delegated(flag)
    assert next(g) == 1
    assert next(g) == 2
    try:
        next(g)
    except StopIteration as exc:
        assert exc.value == (3 if flag else 4)
    else:
        assert False

def pep479():
    yield 1
    raise StopIteration('inner')
g = pep479()
assert next(g) == 1
try:
    next(g)
except RuntimeError as exc:
    assert str(exc) == 'generator raised StopIteration'
else:
    assert False
print('constant return cleanup regression passed')

# Exceptions injected into the suspended cleanup must not restore the pending return.
failure = ValueError('injected during cleanup')
g = suspending()
assert next(g) == 'cleanup'
try:
    g.throw(failure)
except ValueError as exc:
    assert exc is failure
else:
    assert False
g = suspending()
assert next(g) == 'cleanup'
assert g.close() is None
assert next(g, 'closed') == 'closed'
print('suspended constant return exception regression passed')
