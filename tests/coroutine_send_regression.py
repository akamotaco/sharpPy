"""Coroutine entry/resumption compared with CPython 3.12."""
def step(label, coroutine, value):
    try:
        print(label, "yield", coroutine.send(value))
    except StopIteration as error:
        print(label, "return", error.value)
    except Exception as error:
        print(label, type(error).__name__, str(error))

async def immediate():
    return 11

c = immediate()
step("initial-value", c, 1)
step("initial-none", c, None)
step("completed", c, None)
c = immediate()
c.close()
step("closed", c, None)

class Pause:
    def __await__(self):
        first = yield "first"
        print("received-first", first)
        second = yield "second"
        print("received-second", second)
        return 23

async def suspended():
    result = await Pause()
    print("await-result", result)
    return result + 1

c = suspended()
step("start", c, None)
step("resume-value", c, 7)
step("resume-none", c, None)
step("resume-finished", c, None)

async def nested():
    return await suspended()
c = nested()
step("nested-start", c, None)
step("nested-first", c, None)
step("nested-second", c, None)

async def empty():
    pass
step("empty", empty(), None)

async def broken():
    raise ValueError("body error")
c = broken()
step("failure", c, None)
step("failure-reuse", c, None)

async def reentrant():
    step("reentry", c, None)
    return 31
c = reentrant()
step("outer", c, None)

async def stopped():
    raise StopIteration(9)
c = stopped()
step("stop", c, None)
step("stop-reuse", c, None)
async def nested_failure():
    try:
        await broken()
    except ValueError as error:
        print("nested-caught", str(error))
    return 41
step("nested-failure", nested_failure(), None)

def factory(value):
    async def capture():
        await Pause()
        return value
    return capture
c = factory(53)()
step("closure-start", c, None)
step("closure-first", c, None)
step("closure-second", c, None)
print("coroutine send regression complete")
