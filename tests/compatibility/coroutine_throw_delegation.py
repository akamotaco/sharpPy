"""Pending: coroutine.throw must delegate to the suspended await iterator."""
class Pause:
    def __await__(self):
        try:
            yield "paused"
        except ValueError as error:
            print("delegate caught", str(error))
            return 13

async def worker():
    try:
        return await Pause()
    finally:
        print("coroutine cleanup")

c = worker()
print(c.send(None))
try:
    try:
        c.throw(ValueError("sent"))
    except StopIteration as error:
        print("result", error.value)
finally:
    c.close()
