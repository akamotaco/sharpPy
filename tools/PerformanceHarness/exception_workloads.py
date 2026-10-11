def exception_construction(n):
    total = 0
    for i in range(n):
        error = ValueError("message")
        total += i
    return total
