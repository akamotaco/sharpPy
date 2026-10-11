def sort_small(n):
    result = 0
    for i in range(n):
        values = [19, 3, 11, 5, 2, 17, 13, 7]
        values.sort()
        result += i if values[0] == 2 and values[-1] == 19 else -1
    return result

def sort_large(n):
    source = [(i * 73 + 19) % 101 for i in range(256)]
    result = 0
    for i in range(n):
        values = sorted(source)
        result += i if values[0] == 0 and values[-1] == 100 else -1
    return result

def sort_ordered(n):
    source = list(range(256))
    result = 0
    for i in range(n):
        values = sorted(source)
        result += i if values[0] == 0 and values[-1] == 255 else -1
    return result
