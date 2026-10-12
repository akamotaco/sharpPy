"""Legacy harness names; each workload returns sum(range(n))."""
def addition(n):
    total = 0
    for i in range(n):
        format(12.5, '+08.2f')
        total += i
    return total

def inplace_addition(n):
    total = 0
    for i in range(n):
        format(12.5, '.4g')
        total += i
    return total

def function_call(n):
    total = 0
    for i in range(n):
        total += i
    return total
