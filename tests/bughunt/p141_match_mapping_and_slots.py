import functools
@functools.total_ordering
class V:
    def __init__(self, a): self.a = a
    def __eq__(self, o): return self.a == o.a
    def __lt__(self, o): return self.a < o.a
print(V(1) < V(2), V(2) >= V(1), V(1) <= V(1), V(3) > V(1))
class W:
    pass
W.__ge__ = lambda s, o: "ge"
print(W() >= W())
setattr(W, "__lt__", lambda s, o: "lt")
print(W() < W())
print(getattr(V, "__ne__", 0) is getattr(object, "__ne__", 1), object.__lt__ is object.__lt__, V.__gt__ is object.__gt__)
print(V.__hash__ is None, object.__hash__ is object.__hash__)
def m(c):
    match c:
        case {"a": 1, **r}:
            return "one", r
        case {"b": x, "c": y, **r}:
            return x, y, r
        case {**r}:
            return "all", r
        case _:
            return "no"
print(m({"a": 1, "z": 2}), m({"b": 1, "c": 2, "d": 3, "e": 4}), m({"q": 1}), m(5), m({}))
from collections import OrderedDict
print(m(OrderedDict(a=1, b=2)))
from collections.abc import Mapping
class MM(Mapping):
    def __getitem__(self, k): return {"a": 1, "q": 9}[k]
    def __iter__(self): return iter(["a", "q"])
    def __len__(self): return 2
print(m(MM()))
def n(c):
    match c:
        case {"x": 1}:
            return 1
        case {"y": [a, b]}:
            return a + b
        case {"y": {"z": v, **w}}:
            return v, w
        case {"k": int(i) | str(i)}:
            return i
        case _:
            return 0
print(n({"x": 1}), n({"y": [1, 2]}), n({"y": {"z": 3, "o": 1}}), n({"k": 5}), n({"k": "s"}), n({"k": []}), n({}))
from collections.abc import Sequence
class SQ(Sequence):
    def __getitem__(self, i): return [1, 2][i]
    def __len__(self): return 2
class LS(list): pass
class TP(tuple): pass
class DD(dict): pass
def q(c):
    match c:
        case [a, b]:
            return "seq", a, b
        case {"a": v}:
            return "map", v
        case str() | bytes():
            return "text"
        case _:
            return "other"
print(q(SQ()), q(LS([1, 2])), q(TP((1, 2))), q(DD(a=1)), q("ab"), q(b"ab"), q(range(2)), q(iter([1, 2])), q({1, 2}))
