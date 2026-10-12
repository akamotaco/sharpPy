"""Preserved execution gap found while expanding keyword-frame coverage."""
events = []
async def values(*, value=3):
    events.append(value)
    yield value
generator = values(value=7)
request = generator.__anext__()
print('before send', events)
try:
    request.send(None)
except StopIteration as error:
    print('yielded', error.value)
print('after send', events)
