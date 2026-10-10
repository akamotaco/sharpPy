"""SEND must distinguish iterator next from ordinary send lookup."""
events = []
class Iterator:
    def __iter__(self):
        return self
    def __next__(self):
        events.append('next')
        if len(events) == 3:
            raise StopIteration(42)
        return len(events)

def parent(iterator):
    return (yield from iterator)

it = Iterator()
it.__next__ = lambda: 999
g = parent(it)
assert next(g) == 1
assert g.send(None) == 2
try:
    next(g)
except StopIteration as error:
    assert error.value == 42
else:
    assert False
assert events == ['next', 'next', 'next']

g = parent(iter([1, 2]))
assert next(g) == 1
try:
    g.send(8)
except AttributeError as error:
    assert str(error) == "'list_iterator' object has no attribute 'send'"
else:
    assert False

class Sender:
    def __iter__(self):
        return self
    def __next__(self):
        events.append('sender next')
        return 10
    def send(self, value):
        events.append(('send', value))
        raise AttributeError('inside send')
events = []
g = parent(Sender())
assert next(g) == 10
assert g.send(None) == 10
try:
    g.send(5)
except AttributeError as error:
    assert str(error) == 'inside send'
else:
    assert False
assert events == ['sender next', 'sender next', ('send', 5)]

class NextDescriptor:
    def __get__(self, instance, owner):
        events.append('bind next')
        return lambda: 9
class Described:
    __next__ = NextDescriptor()
    def __iter__(self):
        return self
    def __getattribute__(self, name):
        if name in ('__next__', 'send'):
            raise AssertionError('unexpected instance lookup')
        return object.__getattribute__(self, name)
events = []
g = parent(Described())
assert next(g) == 9
assert g.send(None) == 9
g.close()
assert events == ['bind next', 'bind next']
print('generator SEND protocol: PASS')

class ReturningSender(Sender):
    def send(self, value):
        if value == 99:
            raise StopIteration('finished')
        return value * 2
it = ReturningSender()
g = parent(it)
assert next(g) == 10
assert g.send(3) == 6
try:
    g.send(99)
except StopIteration as error:
    assert error.value == 'finished'
else:
    assert False

class FailingNext:
    def __get__(self, instance, owner):
        raise ValueError('next descriptor')
class Broken:
    __next__ = FailingNext()
    def __iter__(self):
        return self
g = parent(Broken())
try:
    next(g)
except ValueError as error:
    assert str(error) == 'next descriptor'
else:
    assert False
print('SEND value and descriptor errors: PASS')
