import asyncio
async def agen(n):
    for i in range(n):
        await asyncio.sleep(0)
        yield i
async def double(x):
    await asyncio.sleep(0)
    return x * 2
async def main():
    print([x async for x in agen(3)])
    print({x async for x in agen(3)})
    print({x: x * 2 async for x in agen(3)})
    print([x async for x in agen(5) if x % 2])
    print([(x, y) async for x in agen(2) for y in range(2)])
    print([(x, y) for x in range(2) async for y in agen(2)])
    print([(x, y) async for x in agen(2) async for y in agen(x + 1)])
    print([await double(x) for x in range(3)])
    print([await double(x) async for x in agen(3)])
    g = (x * 3 async for x in agen(3))
    print(type(g).__name__)
    print([y async for y in g])
    h = (await double(x) for x in range(2))
    print(type(h).__name__)
    print([y async for y in h])
    z = 7
    print([x + z async for x in agen(2)])
    return [x async for x in agen(1)]
print(asyncio.run(main()))
