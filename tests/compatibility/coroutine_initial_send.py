async def result():
    return 11

c = result()
try:
    c.send(None)
except StopIteration as error:
    print(error.value)
