"""Creation, suspension, send/throw/close and asynchronous iteration events."""
events = []
async def values(*, first=3):
    try:
        events.append('started')
        sent = yield first
        events.append(('sent', sent))
        try:
            yield sent
        except ValueError as error:
            events.append(str(error))
            yield 'recovered'
    finally:
        events.append('finally')

def step(request):
    try:
        print('suspended', request.send(None))
    except StopIteration as error:
        print('yielded', error.value)
    except StopAsyncIteration:
        print('exhausted')

generator = values(first=7)
request = generator.__anext__()
print('created', events, iter(request) is request, request.__await__() is request)
step(request)
try: request.send(None)
except RuntimeError as error: print('reuse', str(error))
step(generator.asend(9))
step(generator.athrow(ValueError('injected')))
closing = generator.aclose()
print('close created', events)
step(closing)
print('closed', events)
step(generator.__anext__())

async def pairs():
    yield 1
    yield 2
async def collect():
    result = []
    async for value in pairs(): result.append(value)
    return result
step(collect())

class Pause:
    def __await__(self):
        yield 'pause'
        return 12
async def waits():
    result = await Pause()
    yield result
generator = waits()
request = generator.__anext__()
step(request)
try: generator.__anext__().send(None)
except RuntimeError as error: print('running', str(error))
step(request)
step(generator.__anext__())
print('async generator execution: PASS')

events = []
class AsyncIterator:
    def __init__(self): self.index = 0
    def __aiter__(self):
        events.append('iter')
        return self
    async def __anext__(self):
        events.append(('next', self.index))
        self.index += 1
        if self.index > 2: raise StopAsyncIteration
        return self.index
async def consume():
    result = []
    async for item in AsyncIterator(): result.append(item)
    else: events.append('else')
    return result
step(consume())
print('custom iterator', events)

async def await_value(value): return await value
class BadAwait:
    def __await__(self): raise ValueError('await callback')
class WrongAwait:
    def __await__(self): return []
for value in ([], BadAwait(), WrongAwait()):
    try: await_value(value).send(None)
    except Exception as error: print(type(error).__name__, str(error))

async def stops(exception):
    yield 1
    raise exception
for exception in (StopIteration('stop'), StopAsyncIteration('stop')):
    generator = stops(exception)
    step(generator.__anext__())
    try: generator.__anext__().send(None)
    except RuntimeError as error: print('translated', str(error), type(error.__cause__).__name__)

generator = pairs()
try: generator.asend(1).send(None)
except TypeError as error: print('initial send', str(error))
step(generator.__anext__())
request = generator.aclose()
try: request.send(1)
except RuntimeError as error: print('initial close', str(error))
step(request)
generator = values()
step(generator.aclose())
print('unstarted close', events)
print('stop async args', StopAsyncIteration('a', 'b').args, hasattr(StopAsyncIteration(), 'value'))

generator = pairs()
for descriptor in (type(generator).__anext__, type(generator.__anext__()).__next__):
    try: descriptor(1)
    except TypeError as error: print('descriptor', str(error))
step(generator.aclose())
