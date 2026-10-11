"""Nested await workload; sum of values sent through coroutine_wrapper."""
class Pause:
    def __await__(self):
        value = yield None
        return value

async def accumulate(n):
    total = 0
    for i in range(n):
        total += await Pause()
    return total

async def outer(n):
    return await accumulate(n)

def coroutine_send(n):
    coroutine = outer(n)
    coroutine.send(None)
    for i in range(n):
        try:
            coroutine.send(i)
        except StopIteration as error:
            return error.value
    raise AssertionError("coroutine did not complete")
