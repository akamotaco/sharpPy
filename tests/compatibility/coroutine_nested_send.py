"""Pending: coroutine_wrapper.send must forward non-None values."""
class Pause:
    def __await__(self):
        value = yield "pause"
        return value

async def inner():
    return await Pause()

async def outer():
    return await inner()

c = outer()
print(c.send(None))
try:
    c.send(7)
except StopIteration as error:
    print(error.value)
