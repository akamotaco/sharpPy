"""Function kind is a scope property, independent of remaining YIELD opcodes."""
MASK = 32 | 128 | 512

def kind(function, expected):
    assert function.__code__.co_flags & MASK == expected

normal = lambda value: value + 1
yielding = lambda value: (yield value)
delegating = lambda: (yield from [2, 3])
unreachable = lambda: (yield 1) if False else 42
kind(normal, 0)
kind(yielding, 32)
kind(delegating, 32)
kind(unreachable, 32)
assert normal(4) == 5
g = yielding(7)
assert next(g) == 7
try:
    g.send(9)
    assert False
except StopIteration as error:
    assert error.value == 9
assert list(delegating()) == [2, 3]
try:
    next(unreachable())
    assert False
except StopIteration as error:
    assert error.value == 42

def stop():
    raise StopIteration(5)
pep479 = lambda: (yield stop())
try:
    next(pep479())
    assert False
except StopIteration as error:
    # Actual CPython 3.12 lambda compilation does not add the def wrapper.
    assert error.value == 5

async def leaf():
    return 11

async def waiting():
    return await leaf()

async def values(*, count=3):
    yield count

async def empty():
    return
    yield 1

async def contains_generator():
    def inner():
        yield 1
    return 17

for function in [leaf, waiting, contains_generator]:
    kind(function, 128)
    coroutine = function()
    assert type(coroutine).__name__ == "coroutine"
    coroutine.close()
for function in [values, empty]:
    kind(function, 512)
    assert type(function()).__name__ == "async_generator"
assert values.__code__.co_kwonlyargcount == 1

# Identical names in distinct scopes must not borrow another function's flags.
def first():
    async def same():
        yield 1
    return same

def second():
    async def same():
        return 23
    return same

kind(first(), 512)
kind(second(), 128)
c = second()()
assert type(c).__name__ == "coroutine"
c.close()

async def lambda_factory():
    return lambda: (yield 31)

kind(lambda_factory, 128)
nested_codes = [item for item in lambda_factory.__code__.co_consts if hasattr(item, "co_flags")]
assert len(nested_codes) == 1
assert nested_codes[0].co_flags & MASK == 32
print("generator kind flags: PASS")
