"""Exception depth must preserve enclosing values, not unfinished operands."""
def conditional(flag):
    try:
        if flag:
            return 'early'
    except ValueError:
        return 'wrong'
    return 'fallthrough'
assert [conditional(False), conditional(True)] == ['fallthrough', 'early']

events = []
def nested():
    for i in range(2):
        for j in range(2):
            try:
                value = 100 + (yield (i, j))
                events.append(('value', value))
            except ValueError as error:
                events.append(('caught', str(error)))
            finally:
                events.append(('finally', i, j))
g = nested()
assert next(g) == (0, 0)
assert g.throw(ValueError('first')) == (0, 1)
assert g.send(7) == (1, 0)
assert g.throw(ValueError('third')) == (1, 1)
g.close()
assert events == [('caught', 'first'), ('finally', 0, 0), ('value', 107),
                  ('finally', 0, 1), ('caught', 'third'), ('finally', 1, 0), ('finally', 1, 1)]
print(events)

# Handler failures must restore exception state before entering finally.
events = []
for mode in range(3):
    try:
        try:
            if mode == 0:
                raise ValueError('body')
        except ValueError:
            events.append('handler')
            raise TypeError('handler')
        else:
            events.append('else')
            if mode == 1:
                raise TypeError('else')
        finally:
            events.append('finally')
    except TypeError as error:
        events.append(str(error))
assert events == ['handler', 'finally', 'handler', 'else', 'finally', 'else', 'else', 'finally']
print(events)

events = []
try:
    try:
        pass
    except ValueError:
        pass
    finally:
        events.append('once')
        raise TypeError('finally')
except TypeError as error:
    assert str(error) == 'finally'
assert events == ['once']
print(events)

events = []
class Context:
    def __enter__(self):
        events.append('enter')
        return self
    def __exit__(self, kind, value, traceback):
        events.append('exit' if kind is None else kind.__name__)
        return kind is ValueError

def managed():
    for i in range(2):
        with Context():
            value = 100 + (yield i)
            events.append(value)
g = managed()
assert next(g) == 0
assert g.throw(ValueError('suppressed')) == 1
g.close()
assert events == ['enter', 'ValueError', 'enter', 'GeneratorExit']
print(events)

class FailingExit:
    def __enter__(self):
        return self
    def __exit__(self, kind, value, traceback):
        raise TypeError('exit failure')

events = []
for i in range(2):
    try:
        with FailingExit():
            raise ValueError('body failure')
    except TypeError as error:
        events.append((i, str(error)))
assert events == [(0, 'exit failure'), (1, 'exit failure')]
print(events)
print('exception stack depth: passed')
