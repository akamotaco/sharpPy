# L2.5 measurement, outside the acceptance criteria. Expected mismatch - known_gaps P0-72 to P0-74.
def t(f):
    try:
        print(repr(f()))
    except BaseException as x:
        print(type(x).__name__, x)


class E(Exception):
    pass


t(lambda: print.__qualname__)
t(lambda: [].append.__qualname__)
t(lambda: str.join.__qualname__)
t(lambda: object.__new__(E))
t(lambda: ValueError.__new__(KeyError, 1))
t(lambda: BaseException.__new__(int))
t(lambda: type("X", (int, Exception), {}))
