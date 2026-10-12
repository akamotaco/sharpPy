"""Legacy entry names permit comparison against existing harness binaries."""
def function_call(n):
    def pending(*, value):
        yield value
    result = 0
    for i in range(n):
        result += next(pending(value=i))
    return result
