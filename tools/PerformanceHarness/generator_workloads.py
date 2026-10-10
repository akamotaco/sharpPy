def values(n):
    for i in range(n):
        yield i


def generator_iteration(n):
    total = 0
    for value in values(n):
        total += value
    return total


def closable():
    try:
        yield None
    finally:
        pass


def generator_close(n):
    total = 0
    for i in range(n):
        g = closable()
        next(g)
        g.close()
        total += i
    return total


def delegated(n):
    yield from range(n)


def generator_delegation(n):
    total = 0
    for value in delegated(n):
        total += value
    return total


class PythonIterator:
    def __init__(self):
        self.value = 0
    def __iter__(self):
        return self
    def __next__(self):
        value = self.value
        self.value += 1
        return value


def python_iterator(n):
    iterator = PythonIterator()
    total = 0
    for i in range(n):
        total += next(iterator)
    return total


def iterator_acquisition(n):
    iterator = PythonIterator()
    total = 0
    for i in range(n):
        total += next(iter(iterator))
    return total


class IterableMeta(type):
    def __iter__(cls):
        return iter(range(1))


class ClassIterable(metaclass=IterableMeta):
    pass


def metaclass_iteration(n):
    total = 0
    for i in range(n):
        total += i + next(iter(ClassIterable))
    return total
