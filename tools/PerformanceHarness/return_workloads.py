def zero(flag):
    if flag:
        return 0
    return 0

def cleanup_zero():
    for item in range(1):
        return 0

def constant_return(n):
    total = 0
    for i in range(n):
        total += i + zero(i & 1)
    return total

def constant_cleanup(n):
    total = 0
    for i in range(n):
        total += i + cleanup_zero()
    return total
