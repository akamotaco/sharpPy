immutable = b'abc'
mutable = bytearray(b'abc')

def addition(n):
    total = 0
    for i in range(n):
        view = memoryview(mutable)
        view.release()
        total += i
    return total

def inplace_addition(n):
    total = 0
    for i in range(n):
        view = memoryview(immutable)
        view.release()
        total += i
    return total

def function_call(n):
    total = 0
    for i in range(n):
        total += i
    return total
