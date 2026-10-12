import warnings

events = []
class Matcher:
    def __init__(self, name, result=True):
        self.name, self.result = name, result
    def match(self, text):
        events.append(self.name)
        return self.result
class Line:
    def __index__(self):
        events.append('index')
        return 0
    def __eq__(self, other):
        events.append('eq')
        return True

for line in (Line(), 1.5, '0', 2 ** 100):
    events.clear()
    with warnings.catch_warnings(record=True) as log:
        warnings.filters = [('always', Matcher('message', False), Warning, Matcher('module'), line)]
        try:
            warnings.warn_explicit('test', UserWarning, 'test.py', 1)
            print('line', type(line).__name__, events, len(log))
        except Exception as exc:
            print('line error', type(line).__name__, events, type(exc).__name__, str(exc))

for action in ('always', 12, 'unknown'):
    with warnings.catch_warnings(record=True) as log:
        warnings.filters = []
        old = warnings.defaultaction
        warnings.defaultaction = action
        try:
            warnings.warn_explicit('test', UserWarning, 'test.py', 1)
            print('default', repr(action), len(log))
        except Exception as exc:
            print('default error', repr(action), type(exc).__name__, str(exc))
        finally:
            warnings.defaultaction = old

class Mutate:
    def match(self, text):
        events.append('mutate')
        warnings.filters = [('error', None, Warning, None, 0)]
        active.append(('always', None, Warning, None, 0))
        return False
events.clear()
with warnings.catch_warnings(record=True) as log:
    active = [('ignore', Mutate(), Warning, None, 0)]
    warnings.filters = active
    warnings.warn_explicit('test', UserWarning, 'test.py', 1)
    print('retained filters', events, len(log))
print('dynamic warning filters: PASS')
