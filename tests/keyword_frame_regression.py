"""Keyword metadata must survive deferred execution without an artificial frame."""
events = []
def value(label, result):
    events.append(label)
    return result

def ordinary(a, *, b=2):
    events.append(('ordinary', a, b))
    return a + b

def generator(a, *, b=2):
    events.append(('generator', a, b))
    yield a + b

async def coroutine(a, *, b=2):
    events.append(('coroutine', a, b))
    return a + b

for factory in (ordinary, generator, coroutine):
    events.clear()
    result = factory(value('a', 3), b=value('b', 4))
    print(factory.__name__, 'created', events)
    factory.__kwdefaults__ = {'b': 99}
    if factory is generator:
        print(next(result))
    elif factory is coroutine:
        try:
            result.send(None)
        except StopIteration as error:
            print(error.value)
    else:
        print(result)
    print('executed', events)

def outer(x):
    def inner(*, y):
        yield x + y
    return inner(y=5)
print('closure', next(outer(6)))
print('keyword frames: PASS')
