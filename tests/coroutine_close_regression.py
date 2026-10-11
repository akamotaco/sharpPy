"""Coroutine close/delegation semantics compared with CPython 3.12."""
events = []

class Pause:
    def __await__(self):
        try:
            yield "paused"
        finally:
            events.append("delegate")

async def worker():
    try:
        await Pause()
    finally:
        events.append("worker")

async def outer():
    try:
        await worker()
    finally:
        events.append("outer")

def close(label, obj):
    try:
        print(label, obj.close())
    except BaseException as error:
        print(label, type(error).__name__, str(error))

def send(label, obj):
    try:
        print(label, obj.send(None))
    except StopIteration as error:
        print(label, "return", error.value)
    except BaseException as error:
        print(label, type(error).__name__, str(error))

c = worker()
close("unstarted", c)
print("unstarted-events", events)
send("unstarted-reuse", c)
for wrapped in (False, True):
    events.clear()
    c = outer()
    w = c.__await__()
    send("start", w)
    close("close-wrapper" if wrapped else "close-coroutine", w if wrapped else c)
    print("cleanup-order", events)
    close("repeat", w)
    send("closed-reuse", c)

class Bare:
    def __await__(self):
        return self
    def __iter__(self):
        return self
    def __next__(self):
        return "bare"

class Broken(Bare):
    def close(self):
        events.append("broken-close")
        raise ValueError("delegate error")

class Reentrant(Bare):
    def __init__(self):
        self.active = False
    def close(self):
        if not self.active:
            self.active = True
            close("delegate-reentry-close", c)
            send("delegate-reentry-send", c)
        events.append("reentrant-close")

async def custom(obj):
    try:
        await obj
    except GeneratorExit as error:
        print("exit-args", error.args)
        raise
    finally:
        events.append("custom")

for obj in (Bare(), Broken(), Reentrant()):
    events.clear()
    c = custom(obj)
    send("custom-start", c)
    close("custom-close", c)
    print("custom-events", events)
    close("custom-repeat", c)
    send("custom-reuse", c)

async def swallow():
    try:
        await Broken()
    except ValueError as error:
        print("caught-close-error", str(error))
        return 42
c = swallow()
send("swallow-start", c)
close("swallow-close", c)
send("swallow-reuse", c)

async def ignoring():
    try:
        await Pause()
    finally:
        events.append("cleanup-await")
        await Pause()
        events.append("cleanup-finished")
        return 71
for resume in (False, True):
    events.clear()
    c = ignoring()
    send("ignoring-start", c)
    close("ignoring-first-close", c)
    print("ignoring-events", events)
    if resume:
        send("ignoring-resume", c)
    else:
        close("ignoring-second-close", c)
    print("ignoring-final-events", events)
    close("ignoring-repeat", c)

async def body_reentry():
    close("body-reentry", c)
    return 9
c = body_reentry()
send("body-completion", c)
close("body-completed-close", c)

c = worker()
w = c.__await__()
for obj in (c, w):
    for args, kwargs in (((1,), {}), ((), {"x": 1})):
        try:
            obj.close(*args, **kwargs)
        except TypeError as error:
            print("invalid-close", str(error))
close("after-invalid-close", w)
class Stops(Bare):
    def close(self):
        raise StopIteration(13)

class Exits(Bare):
    def close(self):
        raise GeneratorExit

class ExitInstance(Bare):
    def close(self):
        raise GeneratorExit()

async def ending(obj):
    try:
        value = await obj
        print("after-await", value)
    finally:
        events.append("ending")
for obj in (Stops(), Exits(), ExitInstance()):
    events.clear()
    c = ending(obj)
    send("ending-start", c)
    close("ending-close", c)
    print("ending-events", events)
    send("ending-reuse", c)

async def failing_finally():
    try:
        await Pause()
    finally:
        raise ValueError("cleanup error")
c = failing_finally()
send("finally-failure-start", c)
close("finally-failure", c)
send("finally-failure-reuse", c)
for kind in (BaseException, GeneratorExit):
    try:
        raise kind
    except BaseException as error:
        print("raise-base-class", type(error).__name__, isinstance(error, kind))
    instance = kind("sentinel")
    try:
        raise instance
    except BaseException as error:
        print("raise-base-instance", error is instance)
print("coroutine close regression complete")
