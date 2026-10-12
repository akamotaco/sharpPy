"""Set operations; legacy harness entry names allow comparisons to old binaries."""
def dictionary(n):
    result = 0
    for i in range(n):
        values = {1, 2, 3, 4}
        values.add(5)
        if 3 in values: result += i
        values.remove(2)
    return result

def dictionary_copy(n):
    source = set(range(64))
    result = 0
    for i in range(n):
        if len(source.copy()) == 64: result += i
    return result

def dictionary_iteration(n):
    source = set(range(16))
    result = 0
    for i in range(n):
        if 7 in source: result += i
    return result
