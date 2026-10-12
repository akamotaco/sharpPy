def build():
    def local(): return 17
    class Outer:
        value = local()
        class Inner: pass
        def method(self):
            class Result: pass
            return Result
    print(Outer.value, Outer.__qualname__, Outer.Inner.__qualname__)
    print(Outer().method().__qualname__)
build()

events = []
def default():
    events.append('default')
    return 7
async def async_values(*, count=default()): yield count
async def coroutine(*, count=default()): return count
def generator(*, count=default()): yield count
def regular(*, count=default()): return count
print(events)
for function in (async_values, coroutine, generator, regular):
    print(function.__name__, function.__kwdefaults__)
    try: function.__kwdefaults__ = 1
    except TypeError as error: print(str(error))
    function.__kwdefaults__ = {'count': 9}
print(regular(), next(generator()))
c = coroutine()
try: c.send(None)
except StopIteration as end: print(end.value)
regular.__kwdefaults__ = None
try: regular()
except TypeError as error: print(str(error))
regular.__kwdefaults__ = {'count': 11}
print(regular())
del regular.__kwdefaults__
print(regular.__kwdefaults__)
try: regular()
except TypeError as error: print(str(error))
