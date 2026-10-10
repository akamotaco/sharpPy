def add(a, b):
    return a + b

def select(flag):
    if flag:
        return 1
    return 2

def loop(n):
    total = 0
    for i in range(n):
        total += i
    return total

def protected(x):
    try:
        return 10 // x
    except ZeroDivisionError:
        return -1

def delegated(n):
    yield from range(n)
