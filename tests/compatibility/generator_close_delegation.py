"""Closing a delegating generator must close the live subiterator first."""
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
outer = parent(inner)
assert next(outer) == 1
assert outer.close() is None
assert events == ['child', 'parent']
assert next(inner, 'done') == 'done'
print('generator delegated close: PASS')
