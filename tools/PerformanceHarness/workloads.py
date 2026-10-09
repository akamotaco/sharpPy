"""Shared, deterministic workloads for SharpPy and CPython. No timed output."""


def addition(n):
    result = 0
    for i in range(n):
        result = result + i
    return result


def inplace_addition(n):
    result = 0
    for i in range(n):
        result += i
    return result


def arithmetic(n):
    result = 0
    for i in range(n):
        result = result + i
        result = result - 1
        result = result * 2
        result = result // 2
    return result


def function_call(n):
    def add(a, b):
        return a + b
    result = 0
    for i in range(n):
        result = add(result, i)
    return result


def attributes(n):
    class Value:
        def __init__(self):
            self.value = 0
    obj = Value()
    for i in range(n):
        obj.value = obj.value + i
    return obj.value


def dictionary(n):
    values = {}
    for i in range(n):
        values[i] = i
    result = 0
    for i in range(n):
        result = result + values[i]
    return result


def comprehension(n):
    return sum([i for i in range(n)])
