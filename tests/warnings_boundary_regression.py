"""Caller fallback, numeric conversion, malformed filters and registry timing."""
import warnings

for level in (-1, 0, 1, 1000):
    with warnings.catch_warnings(record=True) as log:
        warnings.simplefilter('always')
        try:
            warnings.warn('depth', stacklevel=level)
            print('depth', level, log[0].filename == 'sys', log[0].lineno if level == 1000 else log[0].lineno > 0)
        except Exception as error: print('depth error', level, type(error).__name__, str(error))

for level in (1.5, '1', 2 ** 100):
    with warnings.catch_warnings(record=True):
        try: warnings.warn('invalid', stacklevel=level)
        except Exception as error: print('level error', type(error).__name__, str(error))

for entries in (None, [()], [('always', None)], [(12, None, Warning, None, 0)]):
    with warnings.catch_warnings(record=True):
        warnings.filters = entries
        try: warnings.warn_explicit('test', UserWarning, 'test.py', 1)
        except Exception as error: print('filter error', type(error).__name__, str(error))

events = []
registry = {'marker': 1}
class Message(UserWarning):
    def __str__(self):
        events.append('marker' in registry)
        return 'message'
with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    warnings.warn_explicit(Message(), UserWarning, 'test.py', 1, registry=registry)
    print('registry timing', events, len(log), 'marker' in registry)

events = []
class FilterList(list):
    def __len__(self): raise AssertionError('list length callback')
    def __iter__(self): raise AssertionError('list iter callback')
    def __getitem__(self, key): raise AssertionError('list item callback')
class FilterTuple(tuple):
    def __len__(self): raise AssertionError('tuple length callback')
    def __iter__(self): raise AssertionError('tuple iter callback')
    def __getitem__(self, key): raise AssertionError('tuple item callback')
class TextMatcher(str):
    def match(self, value):
        events.append('match')
        return True
with warnings.catch_warnings(record=True) as log:
    warnings.filters = FilterList([FilterTuple(('always', TextMatcher('different'), UserWarning, None, 0))])
    warnings.warn_explicit('test', UserWarning, 'test.py', 1)
    print('native filter storage', events, len(log))
