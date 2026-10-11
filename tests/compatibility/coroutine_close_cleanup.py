"""Pending: close must delegate and inject GeneratorExit into suspended frames."""
events = []

class Pause:
    def __await__(self):
        try:
            yield "paused"
        finally:
            events.append("delegate cleanup")

async def worker():
    try:
        await Pause()
    finally:
        events.append("coroutine cleanup")

c = worker()
print(c.send(None))
print(c.close())
print(events)
