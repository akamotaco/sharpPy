for step in [2147483647, -2147483648, 1073741824, -1073741824]:
    items = [0, 1, 2, 3, 4, 5]
    items[1:5:step] = [9] if step > 0 else []
    assert items == ([0, 9, 2, 3, 4, 5] if step > 0 else [0, 1, 2, 3, 4, 5])
    items = [0, 1, 2, 3, 4, 5]
    items[5:0:step] = [9] if step < 0 else []
    assert items == ([0, 1, 2, 3, 4, 9] if step < 0 else [0, 1, 2, 3, 4, 5])

calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        if calls.count(self.name) != 1:
            raise AssertionError('index callback called twice')
        return self.value

items = [0, 1, 2, 3, 4, 5]
items[Index('start', 0):Index('stop', 6):Index('step', 2)] = [9, 8, 7]
assert items == [9, 1, 8, 3, 7, 5]
assert calls == ['step', 'start', 'stop']
calls.clear()
items[Index('start', 5):Index('stop', 0):Index('step', -2)] = [6, 5, 4]
assert items == [9, 4, 8, 5, 7, 6]
assert calls == ['step', 'start', 'stop']
calls.clear()
try:
    items[Index('start', 0):Index('stop', 6):Index('step', 2)] = [1]
except ValueError as error:
    assert str(error) == 'attempt to assign sequence of size 1 to extended slice of size 3'
else:
    raise AssertionError('expected ValueError')
assert calls == ['step', 'start', 'stop']
assert items == [9, 4, 8, 5, 7, 6]
items = [0, 1, 2, 3, 4, 5]
items[::-1] = items
assert items == [5, 4, 3, 2, 1, 0]
items[::-1] = items
assert items == [0, 1, 2, 3, 4, 5]
print('slice assignment indices: PASS')
