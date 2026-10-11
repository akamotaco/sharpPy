class Number(int): pass

def int_subclass_construction(n):
    total = 0
    for i in range(n):
        value = Number('123456789')
        total += i if value == 123456789 else -1
    return total
