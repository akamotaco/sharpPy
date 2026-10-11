"""Native coroutine wrapper send protocol; reference: CPython 3.12."""
class Pause:
    def __await__(self):
        first = yield "ready"
        second = yield first + 1
        return second

async def leaf():
    return await Pause()

async def outer():
    return (await leaf()) + 10

def step(label, send, value):
    try:
        print(label, "yield", send(value))
    except StopIteration as error:
        print(label, "return", error.value)
    except Exception as error:
        print(label, type(error).__name__, str(error))

c = outer()
step("nested-start", c.send, None)
step("nested-value", c.send, 5)
step("nested-return", c.send, 20)
step("nested-reuse", c.send, None)

c = leaf()
w = c.__await__()
w2 = c.__await__()
print("wrapper-type", type(w).__name__)
print("wrapper-bases", tuple(base.__name__ for base in type(w).__mro__))
print("wrapper-iterator", iter(w) is w, w.__iter__() is w, callable(w))
step("wrapper-initial-value", w.send, 3)
print("wrapper-next", next(w))
step("wrapper-second-alias", w2.send, 6)
step("wrapper-return", lambda value: type(w).send(w, value), 30)
step("wrapper-finished-alias", w2.send, None)

async def immediate():
    return 7
c = immediate()
w = c.__await__()
for obj in (c, w):
    for args, kwargs in (((), {}), ((None, 1), {}), ((), {"value": None}), ((None,), {"x": 1})):
        try:
            obj.send(*args, **kwargs)
        except TypeError as error:
            print(type(obj).__name__, str(error))
step("after-invalid-arguments", w.send, None)
try:
    w()
except TypeError as error:
    print("wrapper-call", str(error))
try:
    type(w).send(1, None)
except TypeError as error:
    print("descriptor-receiver", str(error))

async def failing():
    await Pause()
    raise ValueError("after resume")
c = failing()
w = c.__await__()
step("failing-start", w.send, None)
step("failing-middle", w.send, 1)
step("failing-last", w.send, 2)
step("failing-reuse", w.send, None)
print("coroutine wrapper send regression complete")
