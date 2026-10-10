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
