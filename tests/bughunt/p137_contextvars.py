import asyncio
import contextvars

v = contextvars.ContextVar("v", default=1)
w = contextvars.ContextVar("w")
print(v.name, v.get(), v.get(9), w.get(9))
try:
    w.get()
except LookupError:
    print("lookup")
t = v.set(2)
print(v.get(), t.var is v, t.old_value is contextvars.Token.MISSING)
v.reset(t)
print(v.get())
try:
    v.reset(t)
except RuntimeError:
    print("used")
try:
    w.reset(v.set(3))
except ValueError:
    print("other var")

ctx = contextvars.copy_context()
print(v in ctx, len(ctx))
w.set(5)
print(w in ctx, ctx.get(w, "none"))


def change():
    w.set(6)
    v.set(7)
    return w.get(), v.get()


print(ctx.run(change), w.get(), v.get(), ctx[w], ctx[v], sorted(x.name for x in ctx))
print(sorted((k.name, val) for k, val in ctx.items()), sorted(ctx.values()))
print(contextvars.Context().run(lambda: w.get(0)))


def again():
    ctx.run(print, "nested")


try:
    ctx.run(again)
except RuntimeError:
    print("entered")


async def child(n):
    w.set(n)
    await asyncio.sleep(0)
    return w.get()


async def main():
    w.set(100)
    print(await asyncio.gather(child(1), child(2)), w.get())
    t = asyncio.create_task(child(3), context=contextvars.copy_context())
    print(await t, w.get())


asyncio.run(main())
