"""Pending: close lookup errors must go to sys.unraisablehook."""
import sys

events = []
previous = getattr(sys, "unraisablehook", None)
def record(error):
    events.append(("unraisable", type(error.exc_value).__name__, str(error.exc_value)))
sys.unraisablehook = record

class Pause:
    def __await__(self):
        return self
    def __iter__(self):
        return self
    def __next__(self):
        return "paused"
    @property
    def close(self):
        raise ValueError("lookup error")

async def worker():
    try:
        await Pause()
    except BaseException as error:
        events.append(("body", type(error).__name__))
        raise
    finally:
        events.append("finally")

try:
    c = worker()
    print(c.send(None))
    try:
        print("close-result", c.close())
    except BaseException as error:
        print("close-error", type(error).__name__, str(error))
    print(events)
finally:
    if previous is not None:
        sys.unraisablehook = previous
