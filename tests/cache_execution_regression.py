# CACHE-bearing instructions must preserve failure locations and generator state.
def failing():
    len([])
    values = [1]
    return values[2]

try:
    failing()
except IndexError as error:
    tb = error.__traceback__
    while tb.tb_next is not None:
        tb = tb.tb_next
    assert tb.tb_lineno == 5
    print('cache traceback:', tb.tb_lineno)

events = []
class Value:
    def __init__(self):
        self.value = 3

def sequence():
    obj = Value()
    try:
        for i in range(3):
            try:
                obj.value += len([i])
                sent = yield obj.value
                obj.value += sent
            except ValueError:
                events.append('caught')
                obj.value += 10
    finally:
        events.append(obj.value)

g = sequence()
assert next(g) == 4
assert g.send(2) == 7
assert g.send(0) == 8
try:
    g.throw(ValueError('injected'))
    assert False
except StopIteration:
    pass
g = sequence()
assert next(g) == 4
g.close()
assert events == ['caught', 18, 4]
print('cache generator:', events)
