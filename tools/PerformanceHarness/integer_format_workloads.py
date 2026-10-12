"""Use legacy harness names for comparisons against pre-formatting binaries."""
def function_call(n):
    value = 2**1024 + 12345
    result = 0
    for i in range(n):
        if len(bin(value)) == 1027: result += i
    return result

def dictionary(n):
    result = 0
    for i in range(n):
        if len(format(i, '08d')) == 8: result += i
    return result
