events = []
def values():
    events.append('first')
    yield 123
    events.append('second')
    yield 'ok'
try: ','.join(values())
except TypeError as error: print(str(error), events)

events.clear()
def broken():
    events.append('first')
    yield 123
    events.append('failure')
    raise ValueError('iterator failure')
try: ','.join(broken())
except ValueError as error: print(str(error), events)

class Source:
    def __iter__(self):
        events.append('iter')
        return Cursor()
class Cursor:
    def __init__(self): self.position = 0
    def __iter__(self):
        events.append('iter cursor')
        return self
    def __length_hint__(self):
        events.append('hint')
        return 2
    def __next__(self):
        events.append(('next', self.position))
        self.position += 1
        if self.position == 3: raise StopIteration
        return str(self.position)
events.clear()
print('joined', ','.join(Source()), events)
