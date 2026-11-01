from dataclasses import dataclass


class Base:
    __match_args__ = ("a", "b")

    def __init__(self, a, b):
        self.a = a
        self.b = b


class Derived(Base):
    pass


class Plain:
    x = 1


@dataclass
class P:
    x: int
    y: int = 0


def t(c):
    match c:
        case Base(1, b):
            return "base1", b
        case Base(a=2, b=b):
            return "base2", b
        case P(1, y):
            return "p1", y
        case P(x=2):
            return "p2"
        case Plain(x=v):
            return "plain", v
        case bool(v):
            return "bool", v
        case int(v) if v > 0:
            return "int", v
        case float(v) | str(v):
            return "fs", v
        case bytes(v) | bytearray(v):
            return "by", bytes(v)
        case list(v) | tuple(v):
            return "seq", v
        case dict(v) | set(v) | frozenset(v):
            return "col", len(v)
        case object():
            return "obj"


for v in (Base(1, 2), Derived(1, 3), Derived(2, 4), P(1, 5), P(2), P(3), Plain(), True, 5, -1, 1.5, "s", b"b",
          bytearray(b"c"), [1], (2,), {"a": 1}, {1}, frozenset(), None):
    print(t(v))
for bad in ("match 1:\n    case int(1, 2):\n        pass", "match 1:\n    case Base(1, 2, 3):\n        pass",
            "match Base(1, 2):\n    case Base(1, a=2):\n        pass", "match 1:\n    case t():\n        pass",
            "match 1:\n    case Plain(1):\n        pass"):
    try:
        exec(bad)
    except TypeError as e:
        print(e)
