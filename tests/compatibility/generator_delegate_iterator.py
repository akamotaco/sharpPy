"""yield from must support an iterator that has __next__ but no send."""
events = []
class Iterator:
    def __iter__(self):
        return self
    def __next__(self):
        return 7
    def close(self):
        events.append('closed')

def parent():
    yield from Iterator()
g = parent()
assert next(g) == 7
assert g.close() is None
assert events == ['closed']
print('generator delegate iterator: PASS')
