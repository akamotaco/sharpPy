"""Shared, deterministic workloads for SharpPy and CPython. No timed output."""
import sys


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


def dictionary_iteration(n):
    values = {}
    for i in range(n):
        values[i] = i
    result = 0
    for key in values:
        result = result + values[key]
    return result


def namespace_update(n):
    namespace = globals()
    result = 0
    for i in range(n):
        namespace.update({'namespace_updated': i})
        result = result + namespace_updated
    return result


def namespace_read(n):
    namespace = globals()
    namespace['namespace_read_base'] = 0
    result = 0
    for i in range(n):
        result = result + namespace['namespace_read_base'] + i
    return result


def dictionary_copy(n):
    source = {i: i for i in range(100)}
    result = 0
    for i in range(n):
        copied = source.copy()
        result = result + copied[50] + i - 50
    return result


def bound_builtin(n):
    values = []
    append = values.append
    for i in range(n):
        append(i)
    return sum(values)


def locals_observation(n):
    result = 0
    for i in range(n):
        result = result + locals()['i']
    return result


def frame_observation(n):
    frame = sys._getframe()
    result = 0
    for i in range(n):
        result = result + frame.f_locals['i']
    return result


def class_locals_observation(n):
    class Body:
        total = 0
        for i in range(n):
            total = total + locals()['i']
    return Body.total


def exception_handling(n):
    result = 0
    for i in range(n):
        try:
            10 // 0
        except ZeroDivisionError:
            result = result + i
    return result
