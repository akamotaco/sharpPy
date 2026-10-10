async def values(*, count=3):
    yield count

print(values.__kwdefaults__)
