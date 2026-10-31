# A parameter captured by a closure lives in a cell, so a comprehension over it must see a reassignment.
# Inlined comprehensions read the stale argument slot.
def g(a):
    a = [1, 2]
    r = [x for x in a]

    def h():
        return a

    return r, {x for x in a}, {x: 1 for x in a}, list(x for x in a), h()


print(g("zz"))


def k(a):
    a = [a]

    def h():
        return a

    b = a
    return b, [y for y in a]


print(k("q"))


def call_cell(f):
    f = len

    def h():
        return f

    return [f(v) for v in ("a", "bc")], h()("xyz")


print(call_cell(None))


def nested(a):
    a = [[1], [2]]

    def h():
        return a

    return [[y for y in x] for x in a], [v for x in a for v in x]


print(nested(0))
