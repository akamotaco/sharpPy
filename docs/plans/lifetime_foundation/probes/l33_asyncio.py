import asyncio

log = []


async def work(name, delay, result=None):
    log.append(("start", name))
    await asyncio.sleep(delay)
    log.append(("end", name))
    return result if result is not None else name


async def boom():
    await asyncio.sleep(0)
    raise ValueError("boom")


async def main():
    print(await work("a", 0))
    print(await asyncio.gather(work("b", 0.02), work("c", 0.01), work("d", 0)))
    print(log)
    log.clear()
    print(await asyncio.gather())
    print(await asyncio.gather(boom(), work("e", 0), return_exceptions=True))
    try:
        await asyncio.gather(boom(), work("f", 0))
    except ValueError as e:
        print("caught", e)
    print(await asyncio.gather(asyncio.gather(work("g", 0)), work("h", 0)))

    t = asyncio.create_task(work("t", 0.01), name="mine")
    print(t.get_name(), t.done(), isinstance(t, asyncio.Task), isinstance(t, asyncio.Future))
    print(await t, t.done(), t.result(), t.exception(), t.cancelled())

    c = asyncio.create_task(work("x", 10))
    await asyncio.sleep(0)
    print(c.cancel(), c.cancelling())
    try:
        await c
    except asyncio.CancelledError:
        print("cancelled", c.cancelled())

    try:
        await asyncio.wait_for(work("y", 10), 0.01)
    except TimeoutError as e:
        print(type(e).__name__)
    print(await asyncio.wait_for(work("z", 0), 1))
    try:
        async with asyncio.timeout(0.01):
            await asyncio.sleep(10)
    except TimeoutError:
        print("timeout block")

    tasks = [asyncio.create_task(work(str(i), 0.01 * (3 - i))) for i in range(3)]
    done, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
    print(len(done), len(pending), [d.result() for d in done])
    await asyncio.wait(pending)
    print([await f for f in asyncio.as_completed([work("p", 0.02), work("q", 0)])])

    async with asyncio.TaskGroup() as g:
        a = g.create_task(work("ga", 0))
        b = g.create_task(work("gb", 0))
    print(a.result(), b.result())
    try:
        async with asyncio.TaskGroup() as g:
            g.create_task(boom())
    except ExceptionGroup as e:
        print(type(e).__name__, e.message, e.exceptions)

    q = asyncio.Queue(maxsize=1)

    async def producer():
        for i in range(3):
            await q.put(i)

    async def consumer():
        return [await q.get() for _ in range(3)]

    print((await asyncio.gather(producer(), consumer()))[1], q.empty(), q.qsize())
    try:
        q.get_nowait()
    except asyncio.QueueEmpty:
        print("empty")

    ev = asyncio.Event()

    async def waiter():
        await ev.wait()
        return "woken"

    w = asyncio.create_task(waiter())
    await asyncio.sleep(0)
    ev.set()
    print(await w, ev.is_set())

    lock = asyncio.Lock()
    order = []

    async def locked(n):
        async with lock:
            order.append(n)
            await asyncio.sleep(0)

    await asyncio.gather(*(locked(i) for i in range(3)))
    print(order, lock.locked())

    sem = asyncio.Semaphore(2)
    async with sem:
        print(sem.locked(), sem._value)

    loop = asyncio.get_running_loop()
    fut = loop.create_future()
    loop.call_soon(fut.set_result, 5)
    print(await fut, fut.done())
    fut2 = loop.create_future()
    loop.call_later(0.01, fut2.set_exception, KeyError("k"))
    try:
        await fut2
    except KeyError as e:
        print(repr(e))
    try:
        fut.set_result(1)
    except asyncio.InvalidStateError as e:
        print(type(e).__name__, e)
    print(asyncio.current_task() is not None, len(asyncio.all_tasks()) >= 1)
    print(await asyncio.shield(work("s", 0)), await asyncio.to_thread(len, "abc"))
    return "done"


print(asyncio.run(main()))
try:
    asyncio.get_running_loop()
except RuntimeError as e:
    print(e)


async def inner():
    asyncio.run(work("n", 0))


c = inner()
try:
    asyncio.run(c)
except RuntimeError as e:
    print(e)
print(asyncio.iscoroutine(c), asyncio.iscoroutinefunction(inner), asyncio.isfuture(1))


async def agen():
    for i in range(3):
        await asyncio.sleep(0)
        yield i


async def uses():
    print([x async for x in agen()])
    async with Ctx() as v:
        print(v)


class Ctx:
    async def __aenter__(self):
        await asyncio.sleep(0)
        return "in"

    async def __aexit__(self, *a):
        print("out")


asyncio.run(uses())
loop = asyncio.new_event_loop()
print(loop.run_until_complete(work("l", 0)), loop.is_running(), loop.is_closed())
loop.close()
print(loop.is_closed())
