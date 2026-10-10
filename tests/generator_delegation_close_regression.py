"""Delegation close ordering, failures and live subgenerator state."""
events = []
def child():
    try:
        yield 1
    finally:
        events.append('child')
def parent(inner):
    try:
        yield from inner
    finally:
        events.append('parent')
inner = child()
middle = parent(inner)
outer = parent(middle)
assert next(outer) == 1
assert outer.close() is None
assert events == ['child', 'parent', 'parent']
assert next(inner, 'done') == 'done'

failure = ValueError('child cleanup')
def failing():
    try:
        yield 2
    finally:
        events.append('failing')
        raise failure
inner = failing()
outer = parent(inner)
assert next(outer) == 2
try:
    outer.close()
except ValueError as caught:
    assert caught is failure
else:
    assert False
assert events[-2:] == ['failing', 'parent']
assert next(outer, 'done') == 'done'

def handling(inner):
    try:
        yield from inner
    except ValueError as caught:
        assert caught is failure
        events.append('handled')
outer = handling(failing())
assert next(outer) == 2
assert outer.close() is None
assert events[-2:] == ['failing', 'handled']

def refusing():
    try:
        yield 3
    except GeneratorExit:
        yield 4
        yield 5
inner = refusing()
outer = parent(inner)
assert next(outer) == 3
try:
    outer.close()
except RuntimeError as caught:
    assert str(caught) == 'generator ignored GeneratorExit'
else:
    assert False
assert events[-1] == 'parent'
assert next(inner) == 5
assert next(outer, 'done') == 'done'
inner.close()

outer = parent(iter([10, 20]))
assert next(outer) == 10
assert outer.close() is None
assert events[-1] == 'parent'
print('delegated generator close: PASS')

class Delegate:
    def __init__(self, mode):
        self.mode = mode
    def __iter__(self):
        return self
    def __next__(self):
        return 7
    def send(self, value):
        return next(self)
    def close(self):
        events.append('delegate')
        try:
            next(outer)
        except ValueError as error:
            assert str(error) == 'generator already executing'
        else:
            assert False
        if self.mode == 'stop':
            raise StopIteration(42)
        if self.mode == 'error':
            raise AttributeError('from close call')
        return 'ignored return'

def result_parent(inner):
    try:
        value = yield from inner
        events.append(value)
    finally:
        events.append('outer')

for mode in ('return', 'stop', 'error'):
    events = []
    outer = result_parent(Delegate(mode))
    assert next(outer) == 7
    if mode == 'error':
        try:
            outer.close()
        except AttributeError as error:
            assert str(error) == 'from close call'
        else:
            assert False
    else:
        assert outer.close() is None
    assert events == (['delegate', 42, 'outer'] if mode == 'stop' else ['delegate', 'outer'])
print('delegated close callbacks: PASS')
