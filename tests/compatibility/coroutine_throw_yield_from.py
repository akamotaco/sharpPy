"""Pending: throw must traverse yield-from inside an __await__ generator."""
def inner():
    try:
        yield 'paused'
    except ValueError:
        print('inner caught')
        return 31
class Awaitable:
    def __await__(self):
        return (yield from inner())
async def worker():
    return await Awaitable()
c = worker()
print(c.send(None))
try:
    try:
        c.throw(ValueError('sent'))
    except StopIteration as error:
        print('result', error.value)
    except ValueError as error:
        print('escaped', str(error))
finally:
    c.close()
