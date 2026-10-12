"""Filters, registries, capture restoration, regex and native warning sharing."""
import warnings
import sys

with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    warnings.warn('first')
    warnings.warn(UserWarning('second'))
    warnings.warn_explicit('explicit', UserWarning, 'sample.py', 7)
    print([(str(item.message), item.category.__name__) for item in log])
    print(log[2].filename, log[2].lineno)

with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    warnings.filterwarnings('ignore', 'hidden', UserWarning, 'sample')
    warnings.warn_explicit('HIDDEN detail', UserWarning, 'sample.py', 1)
    warnings.warn_explicit('visible', UserWarning, 'sample.py', 1)
    warnings.warn_explicit('hidden', UserWarning, 'other.py', 1)
    print('regex', [str(item.message) for item in log])

for action in ('default', 'module', 'always', 'ignore'):
    with warnings.catch_warnings(record=True) as log:
        warnings.simplefilter(action)
        registry = {}
        for line in (1, 1, 2):
            warnings.warn_explicit('repeat', UserWarning, 'sample.py', line, registry=registry)
        print('filter', action, len(log))

saved = warnings.filters
with warnings.catch_warnings(record=True) as outer:
    warnings.simplefilter('always')
    with warnings.catch_warnings(record=True) as inner:
        warnings.simplefilter('error', UserWarning)
        try: warnings.warn('raised')
        except UserWarning as error: print('error', str(error))
    warnings.warn('restored')
    print('nested', len(inner), [str(item.message) for item in outer])
print('filters restored', warnings.filters is saved)

async def pending(): return 1
with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always', DeprecationWarning)
    coroutine = pending()
    try: coroutine.throw(ValueError, 'legacy')
    except ValueError: pass
    print('native', len(log), log[0].category.__name__, 'deprecated' in str(log[0].message))
print('warnings API: PASS')

events = []
class Matcher:
    def __init__(self, name, result): self.name, self.result = name, result
    def match(self, text):
        events.append(self.name)
        return self.result
with warnings.catch_warnings(record=True) as log:
    warnings.filters[:] = [('always', Matcher('message', False), UserWarning, Matcher('module', True), 0)]
    warnings.warn_explicit('probe', UserWarning, 'probe.py', 1)
    print('filter order', events, len(log))

def same_line(): warnings.warn('same line')
with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('default')
    same_line()
    same_line()
    print('caller registry', len(log), '__warningregistry__' in globals())

with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    def emit(): warnings.warn('caller', stacklevel=2)
    before = sys._getframe().f_lineno
    emit()
    print('stacklevel', log[0].lineno == before + 1)

events = []
with warnings.catch_warnings(record=True) as log:
    warnings.filters[:] = [('always', Matcher('message', True), DeprecationWarning, Matcher('module', True), 0)]
    coroutine = pending()
    try: coroutine.throw(ValueError, 'legacy')
    except ValueError: pass
    print('native filter order', events, len(log))

for action in ('once', 'module'):
    with warnings.catch_warnings(record=True) as log:
        warnings.simplefilter(action)
        first, second = {}, {}
        for registry in (first, first, second):
            warnings.warn_explicit('registry', UserWarning, 'sample.py', 3, registry=registry)
        print('registry action', action, len(log), ('registry', UserWarning) in first)
with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    warnings.warn_explicit('shutdown', UserWarning, 'sample.py', 1, module=None)
    print('explicit None module', len(log))
