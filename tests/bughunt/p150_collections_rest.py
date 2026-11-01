import copy
from collections import ChainMap, Counter, OrderedDict, defaultdict, namedtuple

d = defaultdict(list)
d["a"].append(1)
d["b"]
print(d, d.default_factory, len(d), "c" in d, d.get("c"), dict(d), sorted(d), repr(d))
i = defaultdict(int)
for ch in "hello":
    i[ch] += 1
print(sorted(i.items()), i["z"], isinstance(i, dict), type(i).__name__)
n = defaultdict()
try:
    n["x"]
except KeyError as e:
    print("KeyError", e)
print(n.default_factory, copy.copy(d), copy.deepcopy(d), d.copy(), type(d.copy()).__name__)
print(defaultdict(lambda: 5, a=1)["q"], defaultdict(list, {"x": [1]}), defaultdict(int, [("k", 2)]))


class Tree(defaultdict):
    def __init__(self):
        super().__init__(Tree)


t = Tree()
t["a"]["b"]["c"] = 1
print(t["a"]["b"]["c"], type(t["a"]).__name__, len(t))
try:
    defaultdict(1)
except TypeError as e:
    print(e)


class M(defaultdict):
    def __missing__(self, key):
        return key * 2


print(M()["ab"], d.__missing__("new"), d["new"], defaultdict(int) | {"a": 1}, type(defaultdict(int) | {"a": 1}).__name__)

o = OrderedDict()
o["b"] = 1
o["a"] = 2
o["c"] = 3
print(o, list(o), list(o.items()), list(o.keys()), list(o.values()), list(reversed(o)), repr(o))
o.move_to_end("b")
print(list(o))
o.move_to_end("c", last=False)
print(list(o), o.popitem(), o.popitem(last=False), o)
try:
    o.move_to_end("zz")
except KeyError as e:
    print("KeyError", e)
try:
    OrderedDict().popitem()
except KeyError as e:
    print("KeyError", e)
a = OrderedDict([("x", 1), ("y", 2)])
b = OrderedDict([("y", 2), ("x", 1)])
print(a == b, a == dict(b), dict(a) == dict(b), a != b, isinstance(a, dict), a.copy(), copy.deepcopy(a), OrderedDict.fromkeys("ab", 0))
a.update(z=3)
del a["x"]
a["x"] = 9
print(list(a), a.pop("y"), a.pop("q", "dflt"), a.setdefault("n", 7), len(a), a | {"m": 1})
a.clear()
print(a, not a)

c = Counter("abracadabra")
print(c.most_common(2), c["a"], c["z"], sum(c.values()), sorted(c.elements())[:3], c.total())
e = Counter(a=3, b=1)
f = Counter(a=1, b=2)
print(e + f, e - f, e & f, e | f, +e, -f, e == Counter(b=1, a=3))
e.update("aa")
e.subtract(b=5)
print(e, e.most_common(), dict(e), Counter({"x": 2}), Counter())

P = namedtuple("P", "x y", defaults=[0])
p = P(1)
print(p, p._asdict(), p._replace(x=5), P._fields, P._field_defaults, p == (1, 0), p.x, p[1], tuple(p), P._make([3, 4]), hash(p) == hash((1, 0)))
x, y = p
print(x, y, repr(p), copy.copy(p), copy.deepcopy(p), type(copy.copy(p)).__name__)
try:
    P()
except TypeError as err:
    print(err)
try:
    p.x = 1
except AttributeError:
    print("AttributeError")

cm = ChainMap({"a": 1}, {"a": 2, "b": 3})
print(cm["a"], cm["b"], len(cm), sorted(cm), cm.maps, cm.new_child({"c": 4})["c"], cm.parents, dict(cm))
cm["z"] = 1
print(cm.maps[0], "z" in cm)
