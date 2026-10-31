# L2.5 route: SETUP_ANNOTATIONS and the annotation store must use the namespace the frame stores names
# in (STORE_NAME's namespace, frame f_locals). Function bodies emit neither: compiler_annassign only
# stores for module and class scope, and does not evaluate the annotation in a function.
class A:
    x: int
    y: int = 2


print(A.__annotations__, A.y, hasattr(A, "x"), "__annotations__" in A.__dict__)


class B(A):
    z: str = "a"


print(B.__annotations__, A.__annotations__)


class D:
    __annotations__ = {"k": 1}
    x: int


print(D.__annotations__)


class E(Exception):
    code: int = 3


print(E.__annotations__, E("a").code)


def f():
    x: int
    y: int = 2
    z: undefined_name
    return y


print(f(), f.__annotations__)


class G:
    def m(self):
        v: int = 1
        return v

    n: "forward" = None


print(G().m(), G.__annotations__)


class H:
    i = 0
    a: int = i + 1
    (b): int = 2


print(H.a, H.b, H.__annotations__)

w: int = 1
print(__annotations__)


class T:
    def __init__(self):
        self.a: int = 1
        self.b: int
        self.d = {}
        self.d["k"]: int = 2
        print(self.d)


t = T()
print(t.a, hasattr(t, "b"), t.d)


class N:
    if True:
        p: int = 1
    for _ in (1,):
        q: str


print(N.__annotations__)


def h():
    class L:
        r: int = 1

    return L.__annotations__


print(h())

try:
    if True:
        late: int = 1
    print(__annotations__)
except NameError as x:
    print(x)
