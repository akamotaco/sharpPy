# Known pre-existing failure: starting a native coroutine empties the value stack.
async def calculate():
    return 6


coroutine = calculate()
try:
    coroutine.send(None)
except StopIteration as result:
    assert result.value == 6
    print(result.value)
else:
    raise AssertionError("coroutine did not return its result")
