path = '.artifacts/file_io_benchmark.tmp'
with open(path, 'w') as output:
    output.write('x' * 8192)

def addition(n):
    total = 0
    for i in range(n):
        with open(path, 'r') as source:
            content = source.read()
        if len(content) == 8192:
            total += i
    return total

def function_call(n):
    total = 0
    for i in range(n):
        total += i
    return total
