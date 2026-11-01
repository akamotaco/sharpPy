def f():
    print([x for x in [1]])
    fs = [lambda: x for x in range(3)]
    print([g() for g in fs])
    print({x: 1 for x in [2]}, {x for x in [3]}, [x + 1 for x in [4]])


f()
print([x for x in [1]])
fs = [lambda: x for x in range(3)]
print([g() for g in fs])


def outer(n):
    a = [n + y for y in range(2)]
    b = [(lambda: y + n)() for y in range(2)]
    c = [lambda: y for y in range(2)]
    return a, b, [g() for g in c]


print(outer(1))


def nested():
    r = [[i * j for j in range(2)] for i in range(2)]
    s = [[(lambda: (i, j)) for j in range(1)] for i in range(1)]
    return r, s[0][0]()


print(nested())


def keep():
    x = "outer"
    r = [x for x in [1]]
    return r, x


print(keep())
