"""A function's name must not determine whether its parameters are global."""
value = 1000


def module(value):
    return value + 1


def module_counter(value=5):
    return value + 1


def abc(value):
    return value + 1


def contextlib(value):
    return value + 1


def functools(value):
    return value + 1


def typing(value):
    return value + 1


for function in [module, module_counter, abc, contextlib, functools, typing]:
    assert function(5) == 6
    assert value == 1000, function.__name__

assert module_counter() == 6
assert module_counter(value=7) == 8
assert value == 1000


def module_locals(value):
    return locals()["value"]


assert module_locals(9) == 9
assert value == 1000


def module_closure(value):
    def captured():
        return value
    return captured


assert module_closure(12)() == 12
assert value == 1000

self = "global self"


class Example:
    def abc(self, value):
        return value + 1


assert Example().abc(5) == 6
assert self == "global self"
assert value == 1000


def module_global_assignment():
    global value
    value = 2000


module_global_assignment()
assert value == 2000


def 모듈(value):
    return value + 1


class 예제:
    값 = 7
    def 메서드(self, value):
        return self.값 + value


assert 모듈(3) == 4
assert 예제().메서드(5) == 12
assert value == 2000
print("scope binding regression: PASS")
