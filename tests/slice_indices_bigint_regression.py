huge = 10 ** 100
unbound = slice.indices
assert unbound(slice(None), huge) == (0, huge, 1)

def expected_indices(start, stop, step, length):
    if step is None:
        step = 1
    low = -1 if step < 0 else 0
    high = length - 1 if step < 0 else length
    if start is None:
        start = high if step < 0 else low
    else:
        if start < 0:
            start += length
        start = min(high, max(low, start))
    if stop is None:
        stop = low if step < 0 else high
    else:
        if stop < 0:
            stop += length
        stop = min(high, max(low, stop))
    return (start, stop, step)

count = 0
for length in (0, 6, 2 ** 63, huge):
    for start in (None, -huge - 1, -1, 0, huge + 1):
        for stop in (None, -huge - 1, -1, 0, huge + 1):
            for step in (None, -huge, -1, 1, huge):
                assert slice(start, stop, step).indices(length) == expected_indices(start, stop, step, length)
                count += 1
assert count == 500
assert slice(True, False, True).indices(True) == (1, 0, 1)

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value

value = slice(Index('start', -huge), Index('stop', huge), Index('step', huge))
bound = value.indices
assert bound(Index('length', huge)) == (0, huge, huge)
assert calls == ['length', 'step', 'start', 'stop']
calls.clear()
assert type(value).indices(value, Index('length', huge)) == (0, huge, huge)
assert calls == ['length', 'step', 'start', 'stop']

class Raised:
    def __index__(self):
        calls.append('raised')
        raise RuntimeError('index failed')

def error(label, action):
    calls.clear()
    try:
        action()
    except (TypeError, ValueError, RuntimeError) as exc:
        print(label, type(exc).__name__, str(exc), calls)
    else:
        raise AssertionError('expected error: ' + label)

error('negative length first', lambda: slice(Raised(), Raised(), Raised()).indices(Index('length', -1)))
error('invalid length first', lambda: slice(Raised(), Raised(), Raised()).indices(None))
error('length exception first', lambda: slice(Raised(), Raised(), Raised()).indices(Raised()))
error('step zero first', lambda: slice(Raised(), Raised(), Index('step', 0)).indices(Index('length', 2)))
error('invalid step', lambda: slice(Raised(), Raised(), 1.5).indices(2))
error('start failure', lambda: slice(Raised(), Index('stop', 2), Index('step', 1)).indices(2))
error('stop failure', lambda: slice(Index('start', 0), Raised(), Index('step', 1)).indices(2))
error('bad index result', lambda: slice(Index('start', 'bad'), None).indices(2))
error('invalid stop', lambda: slice(Index('start', 0), 1.5, Index('step', 1)).indices(2))
error('missing argument', lambda: value.indices())
error('extra argument', lambda: value.indices(1, 2))
error('keyword', lambda: value.indices(length=2))
error('bad receiver', lambda: type(value).indices(1, 2))
error('no receiver', lambda: slice.indices())
error('list no receiver', lambda: list.append())
error('str no receiver', lambda: str.upper())
print('slice bigint indices: PASS')
