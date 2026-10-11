"""Single-argument coroutine throw protocol, CPython 3.12 oracle."""
events = []
class Pause:
    def __await__(self):
        try:
            yield 'pause'
        except ValueError as error:
            events.append(('caught', str(error)))
            yield 'caught-pause'
            return 13
        finally:
            events.append('delegate-finally')
async def worker(awaitable):
    try:
        return await awaitable
    finally:
        events.append('worker-finally')
def invoke(label, method, *args, **kwargs):
    try:
        print(label, 'yield', method(*args, **kwargs))
    except StopIteration as error:
        print(label, 'return', error.value)
    except BaseException as error:
        print(label, type(error).__name__, str(error))
for wrapped in (False, True):
    events.clear()
    c = worker(Pause())
    target = c.__await__() if wrapped else c
    invoke('start', target.send, None)
    invoke('throw-instance', target.throw, ValueError('sent'))
    invoke('resume', target.send, None)
    print('events', events)
    invoke('reuse', target.throw, ValueError('again'))
    invoke('reuse-invalid', target.throw, 17)
    c.close()

# Native await chains forward to the innermost suspension.
async def outer():
    return await worker(Pause())
c = outer()
invoke('nested-start', c.send, None)
invoke('nested-throw', c.throw, ValueError('nested'))
invoke('nested-return', c.send, None)
c.close()

# A native __await__ generator must construct the actual exception class.
c = worker(Pause())
invoke('class-start', c.send, None)
invoke('class-throw', c.throw, ValueError)
invoke('class-return', c.send, None)
c.close()
print('class-events', events)

# Before first execution, neither the body nor its finally executes.
events.clear()
c = worker(Pause())
error = ValueError('before')
try:
    c.throw(error)
except ValueError as caught:
    print('unstarted-identity', caught is error)
print('unstarted-events', events)
invoke('unstarted-reuse', c.send, None)
c.close()

class Iterator:
    def __await__(self): return self
    def __iter__(self): return self
    def __next__(self): return 'iterator-pause'
    def close(self): events.append('iterator-close')
class Missing(Iterator): pass
class Receiver(Iterator):
    def throw(self, *args):
        print('raw-args', args)
        return 'delegate-value'
class LookupFailure(Iterator):
    @property
    def throw(self): raise LookupError('lookup')
class CallFailure(Iterator):
    def throw(self, error): raise LookupError('call')
class Finish(Iterator):
    def throw(self, error): raise StopIteration(29)
for cls in (Missing, Receiver, LookupFailure, CallFailure, Finish):
    events.clear()
    c = worker(cls())
    invoke(cls.__name__, c.send, None)
    invoke('inject', c.throw, ValueError('value'))
    invoke('after', c.send, None)
    c.close()
    print('events', events)

# Invalid exception objects are delegated before local validation.
c = worker(Receiver())
invoke('raw-start', c.send, None)
invoke('raw-invalid', c.throw, 42)
c.close()
for value in (42, ValueError, ValueError('instance'), GeneratorExit, GeneratorExit()):
    events.clear()
    c = worker(Missing())
    invoke('missing-start', c.send, None)
    invoke('missing-inject', c.throw, value)
    c.close()
    print('missing-events', events)

# Reentry from delegate.throw must not leave a pending exception behind.
class Reenter(Iterator):
    def throw(self, error):
        invoke('reentrant-send', c.send, None)
        return 'reentrant-yield'
c = worker(Reenter())
invoke('reentrant-start', c.send, None)
invoke('reentrant-throw', c.throw, ValueError('value'))
invoke('reentrant-resume', c.send, None)
c.close()

for wrapped in (False, True):
    c = worker(Missing())
    target = c.__await__() if wrapped else c
    invoke('no-args', target.throw)
    invoke('too-many', target.throw, 1, 2, 3, 4)
    invoke('keyword', target.throw, ValueError('x'), value=None)
    c.close()

# Constructor failure is injected into the suspended frame, not raised at lookup.
class BadError(ValueError):
    def __init__(self): raise LookupError('constructor')
async def catch_error():
    try:
        await Missing()
    except LookupError as error:
        return str(error)
c = catch_error()
invoke('constructor-start', c.send, None)
invoke('constructor-throw', c.throw, BadError)
invoke('constructor-reuse', c.send, None)
c.close()

# The supplied exception and its existing traceback are retained.
try:
    raise ValueError('trace')
except ValueError as error:
    original = error
    old_tb = error.__traceback__
c = worker(Missing())
c.send(None)
try:
    c.throw(original)
except ValueError as error:
    tb = error.__traceback__
    found = False
    while tb is not None:
        if tb is old_tb: found = True
        tb = tb.tb_next
    print('traceback-preserved', error is original, found)
c.close()

class CloseFailure(Iterator):
    def close(self): raise LookupError('close-error')
async def catch_close():
    try:
        await CloseFailure()
    except LookupError as error:
        return str(error)
c = catch_close()
invoke('close-error-start', c.send, None)
invoke('close-error-throw', c.throw, GeneratorExit)
c.close()

# The same normalization is used by direct generator.throw.
def generator():
    try:
        yield 'ready'
    except ValueError as error:
        print('generator-caught', type(error).__name__, str(error))
        yield 'handled'
for value in (ValueError, ValueError('direct'), GeneratorExit(), 8):
    g = generator()
    invoke('generator-start', next, g)
    invoke('generator-throw', g.throw, value)
    g.close()
    invoke('generator-finished', g.throw, value)
