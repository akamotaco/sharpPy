"""Recursive yield-from throw compared with CPython 3.12."""
events = []
def inner():
    try:
        try:
            yield 'inner'
        except ValueError as error:
            print('caught', str(error))
            yield 'handled'
            return 23
    finally:
        events.append('inner')
def middle(target):
    try:
        return (yield from target)
    finally:
        events.append('middle')
def outer(target):
    try:
        return (yield from middle(target))
    finally:
        events.append('outer')
def call(label, method, *args):
    try:
        print(label, 'yield', method(*args))
    except StopIteration as error:
        print(label, 'return', error.value)
    except BaseException as error:
        print(label, type(error).__name__, str(error))
for error in (ValueError('sent'), ValueError, GeneratorExit(), GeneratorExit):
    events.clear()
    g = outer(inner())
    call('start', next, g)
    call('throw', g.throw, error)
    call('resume', next, g)
    g.close()
    print('cleanup', events)

class Iterator:
    def __iter__(self): return self
    def __next__(self): return 'iterator'
    def close(self): events.append('close')
class Missing(Iterator): pass
class Receiver(Iterator):
    def throw(self, *args):
        print('raw', args)
        return 'received'
class LookupFailure(Iterator):
    @property
    def throw(self): raise LookupError('lookup')
class CallFailure(Iterator):
    def throw(self, error): raise LookupError('call')
class Finish(Iterator):
    def throw(self, error): raise StopIteration(37)
class CloseFailure(Iterator):
    def close(self): raise LookupError('close')
for cls in (Missing, Receiver, LookupFailure, CallFailure, Finish, CloseFailure):
    events.clear()
    g = middle(cls())
    call(cls.__name__, next, g)
    call('inject', g.throw, ValueError('value'))
    call('after', next, g)
    g.close()
    print('events', events)

# Lookup failure propagates through a native delegate's throw call. The outer
# frame finishes, while a retained inner generator can still be resumed.
i = middle(LookupFailure())
g = outer(i)
call('lookup-nested-start', next, g)
call('lookup-nested', g.throw, ValueError('nested'))
call('lookup-inner-resume', next, i)
i.close()
g.close()

# Invalid exception objects are passed to the delegate before normalization.
g = outer(Receiver())
call('raw-start', next, g)
call('raw-invalid', g.throw, 42)
g.close()

# GeneratorExit uses close, and a close error can be handled by the delegator.
def catch_close():
    try:
        yield from CloseFailure()
    except LookupError as error:
        yield str(error)
        return 41
g = outer(catch_close())
call('close-start', next, g)
call('close-throw', g.throw, GeneratorExit)
call('close-resume', next, g)
g.close()
g = catch_close()
call('direct-close-start', next, g)
call('direct-close-throw', g.throw, GeneratorExit)
call('direct-close-resume', next, g)
g.close()

# Reentry must restore frame state and leave no injected exception behind.
class Reenter(Iterator):
    def throw(self, error):
        call('reenter-next', next, g)
        return 'reentered'
g = outer(Reenter())
call('reentry-start', next, g)
call('reentry-throw', g.throw, ValueError('x'))
call('reentry-resume', next, g)
g.close()

# A value returned by delegated throw is still a suspension; send reaches it.
def send_inner():
    try:
        yield 'first'
    except ValueError:
        value = yield 'second'
        return value
g = outer(send_inner())
call('send-start', next, g)
call('send-throw', g.throw, ValueError('x'))
call('send-value', g.send, 51)
g.close()
