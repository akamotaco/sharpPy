# L2.5 measurement, outside the acceptance criteria. Expected mismatch - known_gaps P0-72 to P0-75.
def t(f):
    try:
        print(repr(f()))
    except BaseException as x:
        print(type(x).__name__, x)


class E(Exception):
    pass


class P:
    pass


t(lambda: print.__qualname__)
t(lambda: [].append.__qualname__)
t(lambda: str.join.__qualname__)
t(lambda: object.__new__(E))
t(lambda: ValueError.__new__(KeyError, 1))
t(lambda: BaseException.__new__(int))
t(lambda: type("X", (int, Exception), {}))
p = P()
p.__dict__ = {"a": 1}
t(lambda: p.a)
