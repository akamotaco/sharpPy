from dataclasses import (
    FrozenInstanceError, InitVar, KW_ONLY, asdict, astuple, dataclass, field, fields, is_dataclass,
    make_dataclass, replace,
)
from typing import ClassVar, List, Optional


@dataclass
class P:
    x: int
    y: int = 2
    tags: List[str] = field(default_factory=list)
    hidden: int = field(default=0, repr=False, compare=False)
    count: ClassVar[int] = 0


p = P(1)
print(p, p == P(1), p != P(2), P.__doc__, P.__match_args__, P.count)
print([f.name for f in fields(p)], is_dataclass(p), is_dataclass(P), is_dataclass(1), asdict(p), astuple(p))
print(replace(p, x=9), hasattr(P, "__hash__"), P.__hash__)


@dataclass(order=True)
class O:
    a: int
    b: str = "z"


print(O(1) < O(2), O(1, "a") <= O(1, "b"), sorted([O(3), O(1), O(2)]), max(O(1), O(2)))
try:
    O(1) < 1
except TypeError as e:
    print(e)


@dataclass(frozen=True)
class F:
    v: int


f = F(1)
print(hash(f) == hash(F(1)), {f: 1}[F(1)])
try:
    f.v = 2
except FrozenInstanceError as e:
    print(type(e).__name__, e)
try:
    del f.v
except FrozenInstanceError as e:
    print(e)


@dataclass
class Post:
    a: int
    b: int = field(init=False)
    scale: InitVar[int] = 2

    def __post_init__(self, scale):
        self.b = self.a * scale


print(Post(3), Post(3, 4), Post(1).b)


@dataclass
class Base:
    a: int = 1


@dataclass
class Child(Base):
    b: int = 2
    a: int = 5


print(Child(), [f.name for f in fields(Child)])


@dataclass(kw_only=True)
class K:
    a: int = 1


@dataclass
class K2:
    a: int
    _: KW_ONLY
    b: int = 2


print(K(a=3), K2(1, b=5))
for call in (lambda: K(1), lambda: K2(1, 2), lambda: P(), lambda: P(1, 2, [], 3, 4)):
    try:
        call()
    except TypeError as e:
        print(e)


@dataclass(slots=True)
class S:
    a: int
    b: int = 2


s = S(1)
print(s, S.__slots__, hasattr(s, "__dict__"))
try:
    s.c = 1
except AttributeError as e:
    print(type(e).__name__)


try:
    @dataclass
    class Bad:
        a: list = []
except ValueError as e:
    print(e)
try:
    @dataclass
    class Bad2:
        a: int = 1
        b: int
except TypeError as e:
    print(e)

M = make_dataclass("M", [("x", int), ("y", int, field(default=3))], namespace={"total": lambda self: self.x + self.y})
print(M(1), M(1).total())


@dataclass(eq=False, repr=False)
class N:
    a: int = 1


print(N() == N(), "N object" in repr(N()))


@dataclass
class Nest:
    p: P
    o: Optional[O] = None


print(asdict(Nest(P(1), O(2))), astuple(Nest(P(1))))


@dataclass
class Rec:
    me: object = None


r = Rec()
r.me = r
print(r)


match P(1, 2):
    case P(a, b):
        print("matched", a, b)
