def addition(n):
    total = 0
    for i in range(n):
        value = '{0:08.2f}'.format(12.5)
        total += i
    return total

def inplace_addition(n):
    total = 0
    for i in range(n):
        value = '{name}:{0}'.format(12, name='item')
        total += i
    return total

def function_call(n):
    total = 0
    for i in range(n):
        total += i
    return total
