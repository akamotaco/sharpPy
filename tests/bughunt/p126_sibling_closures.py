# symtable collects the whole tree first and resolves afterwards (Python/symtable.c symtable_analyze), so a
# nested function can name a sibling defined later. Resolving at the end of each function bound it as global.
def make():
    def a(n):
        return b(n) + 1

    def b(n):
        return 1 if n == 0 else a(n - 1)

    return a


print(make()(3), make.__code__.co_cellvars, make().__code__.co_freevars)


def outer():
    f = lambda: later
    g = [lambda: later for _ in range(2)]

    def gen():
        yield later

    later = 5
    return f(), g[0](), next(gen())


print(outer())


class K:
    def method(self):
        def x():
            return y()

        def y():
            return 7

        return x()


print(K().method())


def deep():
    def one():
        def two():
            return three()

        return two()

    def three():
        return 3

    return one()


print(deep())
import json

print(json.dumps({"a": [1, {"b": 2}], "c": {"d": [3]}}))
print(json.dumps({"a": [1, {"b": 2}]}, indent=2, sort_keys=True))
