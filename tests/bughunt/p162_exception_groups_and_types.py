class EG(ExceptionGroup):
    def __new__(cls, m, e, code=0):
        o = super().__new__(cls, m, e)
        o.code = code
        return o
    def derive(self, e):
        return EG(self.message, e, self.code)
try:
    raise EG("m", [ValueError(1), TypeError(2)], 7)
except* ValueError as g:
    print(type(g).__name__, g.code, g.message, g.exceptions)
except* TypeError as g:
    print(type(g).__name__, g.exceptions)
e = EG("m", [ValueError(1)], 3)
print(e.message, e.exceptions, e.split(ValueError)[0].code, e.subgroup(ValueError).code)
try:
    (1).x
except AttributeError as a:
    print(a.name, a.obj)
class Q:
    pass
try:
    Q().y
except AttributeError as a:
    print(a.name, type(a.obj).__name__)
class O(OSError):
    def __init__(self, a, b):
        pass
print(O(1, 2).args, O(1, 2).errno)
def g(x):
    return [x for x in x]
print(g([1]))
print(enumerate, type(enumerate([])), isinstance(enumerate([]), enumerate), super, type(super(int, 1)), memoryview, type(memoryview(b"")))
class E2(enumerate):
    pass
print(list(E2("ab", 1)))
