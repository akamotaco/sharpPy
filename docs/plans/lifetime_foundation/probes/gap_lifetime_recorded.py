# NOT acceptance. Differences found in review on 85bd172, recorded in docs/known_gaps.md P0-51.
# Run it to see the state; do not fix a line here without a queue item that names it.
import gc

log = []


def show(title):
    gc.collect()
    print(title, log)
    log.clear()


class Meta(type):
    def __del__(cls):
        log.append("Meta")


def make():
    class X(metaclass=Meta):
        pass


make()
show("metaclass __del__:")


class aw:
    def __await__(self):
        yield 1


async def co():
    try:
        await aw()
    finally:
        log.append("co finally")


c = co()
c.send(None)
del c
show("suspended coroutine:")
