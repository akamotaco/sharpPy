def closure_access(n):
    value = 0
    def read():
        return value
    total = 0
    for value in range(n):
        total += read()
    return total


def nonlocal_update(n):
    value = -1
    def advance():
        nonlocal value
        value += 1
        return value
    total = 0
    for i in range(n):
        total += advance()
    return total
