"""Function creation must retain defaults, metadata, cells and async code flags."""


def annotated(value: int = 3, *, extra: int = 5) -> int:
    return value + extra


assert annotated() == 8
assert annotated(7, extra=2) == 9
assert annotated.__defaults__ == (3,)
assert annotated.__kwdefaults__ == {"extra": 5}
assert annotated.__annotations__ == {"value": int, "extra": int, "return": int}


def make_counter(initial):
    count = initial
    def increment(delta=1):
        nonlocal count
        count += delta
        return count
    return increment


first = make_counter(10)
second = make_counter(100)
assert first() == 11
assert second(5) == 105
assert first(2) == 13

functions = []
for index in range(4):
    def captured_default(value=index):
        return value
    functions.append(captured_default)
assert [function() for function in functions] == [0, 1, 2, 3]


def make_closures():
    functions = []
    for index in range(3):
        def shared_cell():
            return index
        functions.append(shared_cell)
    return functions


assert [function() for function in make_closures()] == [2, 2, 2]


def make_generator(multiplier):
    def values(limit=3):
        for value in range(limit):
            yield multiplier * value
    return values


assert list(make_generator(4)()) == [0, 4, 8]
assert list(make_generator(7)(2)) == [0, 7]


async def calculate(value=4):
    return value + 2


coroutine = calculate()
assert calculate.__defaults__ == (4,)
assert calculate.__code__.co_flags & 128
coroutine.close()


async def async_values():
    yield 1


assert async_values.__code__.co_flags & 512
print("function creation regression: PASS")
