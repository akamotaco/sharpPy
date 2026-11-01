# binary_op1 and slot_nb_*: who is asked, in which order, and what NotImplemented does.
log = []


def trace(name, result=NotImplemented):
    def method(self, other):
        log.append((type(self).__name__, name, type(other).__name__))
        return result
    return method


class Base:
    __add__ = trace("__add__")
    __radd__ = trace("__radd__")
    __iadd__ = trace("__iadd__")


class Same(Base):
    pass


class Over(Base):
    __radd__ = trace("__radd__ Over", "from Over")


class Only(Base):
    __add__ = trace("__add__ Only", "from Only")


class Unrelated:
    __radd__ = trace("__radd__ Unrelated", "from Unrelated")


class NoRadd:
    __radd__ = None


class Number(int):
    __radd__ = trace("__radd__ Number", "from Number")
    __add__ = trace("__add__ Number", "from add")


class Text(str):
    __radd__ = trace("__radd__ Text", "from Text")
    __rmod__ = trace("__rmod__ Text", "from rmod")
    __rmul__ = trace("__rmul__ Text", "from rmul")


class Items(list):
    __radd__ = trace("__radd__ Items", "from Items")
    __rmul__ = trace("__rmul__ Items", "from rmul")


class Pair(tuple):
    __radd__ = trace("__radd__ Pair", "from Pair")


class Rmul:
    __rmul__ = trace("__rmul__", "rmul")
    __rpow__ = trace("__rpow__", "rpow")
    __rmatmul__ = trace("__rmatmul__", "rmatmul")
    __rlshift__ = trace("__rlshift__", "rlshift")
    __rand__ = trace("__rand__", "rand")


class Index:
    def __index__(self):
        return 2


def attempt(label, operation):
    del log[:]
    try:
        print(label, repr(operation()), log)
    except Exception as e:
        print(label, type(e).__name__, e, log)


attempt("same", lambda: Base() + Base())
attempt("sibling", lambda: Same() + Base())
attempt("over right", lambda: Base() + Over())
attempt("only right", lambda: Base() + Only())
attempt("over left", lambda: Over() + Base())
attempt("unrelated", lambda: Base() + Unrelated())
attempt("left none", lambda: None + Unrelated())
attempt("left object", lambda: object() + Unrelated())
attempt("left int", lambda: 1 + Unrelated())
attempt("left str", lambda: "a" + Unrelated())
attempt("left list", lambda: [] + Unrelated())
attempt("left tuple", lambda: () + Unrelated())
attempt("left dict", lambda: {} + Unrelated())
attempt("left bytes", lambda: b"" + Unrelated())
attempt("radd none", lambda: 1 + NoRadd())
attempt("radd none str", lambda: "a" + NoRadd())
attempt("int number", lambda: 1 + Number(2))
attempt("number int", lambda: Number(2) + 1)
attempt("number number", lambda: Number(1) + Number(2))
attempt("float number", lambda: 1.5 + Number(2))
attempt("str text", lambda: "a" + Text("b"))
attempt("text str", lambda: Text("b") + "a")
attempt("str mod text", lambda: "%s" % Text("b"))
attempt("int mul text", lambda: 2 * Text("b"))
attempt("list items", lambda: [1] + Items([2]))
attempt("items list", lambda: Items([2]) + [1])
attempt("int mul items", lambda: 2 * Items([1]))
attempt("tuple pair", lambda: (1,) + Pair((2,)))
attempt("list mul rmul", lambda: [1] * Rmul())
attempt("str mul rmul", lambda: "a" * Rmul())
attempt("int mul rmul", lambda: 2 * Rmul())
attempt("int pow", lambda: 2 ** Rmul())
attempt("int matmul", lambda: 2 @ Rmul())
attempt("int shift", lambda: 2 << Rmul())
attempt("int and", lambda: 2 & Rmul())
attempt("set and", lambda: {1} & Rmul())
attempt("list mul index", lambda: [1] * Index())
attempt("index mul list", lambda: Index() * [1])
attempt("str mul index", lambda: Index() * "a")
attempt("list mul float", lambda: [1] * 1.5)
attempt("str add int", lambda: "a" + 1)
attempt("int add str", lambda: 1 + "a")
attempt("list add tuple", lambda: [1] + (2,))
attempt("none add none", lambda: None + None)
attempt("object add object", lambda: object() + object())


def inplace(start, other):
    value = start
    value += other
    return value


attempt("iadd same", lambda: inplace(Base(), Base()))
attempt("iadd over", lambda: inplace(Base(), Over()))
attempt("iadd unrelated", lambda: inplace(Base(), Unrelated()))
attempt("iadd list", lambda: inplace([1], (2,)))
attempt("iadd int", lambda: inplace(1, Unrelated()))
attempt("iadd str", lambda: inplace("a", Unrelated()))
attempt("iadd list int", lambda: inplace([1], 2))


class Instance:
    pass


holder = Instance()
holder.__add__ = lambda other: "instance"
attempt("instance dunder", lambda: holder + 1)


class Meta(type):
    def __add__(cls, other):
        return "meta add"

    def __radd__(cls, other):
        return "meta radd"


class WithMeta(metaclass=Meta):
    pass


attempt("class add", lambda: WithMeta + 1)
attempt("class radd", lambda: 1 + WithMeta)


class Raises:
    def __radd__(self, other):
        raise KeyError("from radd")

    def __add__(self, other):
        raise ValueError("from add")


attempt("raises add", lambda: Raises() + 1)
attempt("raises radd", lambda: 1 + Raises())
print(range(2).index.__name__)
for call in (lambda: range(2).index(), lambda: range(2).count(), lambda: range(2).index(1, 2), lambda: [].index(),
             lambda: "a".find(), lambda: {}.get(), lambda: set().add(), lambda: (1,).count(), lambda: dict.fromkeys()):
    try:
        print(call())
    except Exception as e:
        print(type(e).__name__, e)


class SuperInt(int):
    def __add__(self, other):
        return ("int", super().__add__(other))

    def __radd__(self, other):
        return ("rint", super().__radd__(other))


class SuperStr(str):
    def __add__(self, other):
        return ("str", super().__add__(other))

    def __mul__(self, other):
        return ("mul", super().__mul__(other))


class SuperList(list):
    def __add__(self, other):
        return ("list", super().__add__(other))

    def __iadd__(self, other):
        return ("iadd", super().__iadd__(other))


class SuperTuple(tuple):
    def __add__(self, other):
        return ("tuple", super().__add__(other))


class Declines(list):
    def __add__(self, other):
        return NotImplemented

    def __mul__(self, other):
        return NotImplemented


class DeclinesTuple(tuple):
    def __add__(self, other):
        return NotImplemented


attempt("super int", lambda: SuperInt(1) + 2)
attempt("super int r", lambda: 2 + SuperInt(1))
attempt("super str", lambda: SuperStr("a") + "b")
attempt("super mul", lambda: SuperStr("a") * 2)
attempt("super list", lambda: SuperList([1]) + [2])
attempt("super iadd", lambda: inplace(SuperList([1]), [2]))
attempt("super tuple", lambda: SuperTuple((1,)) + (2,))
attempt("declines", lambda: Declines([1]) + [2])
attempt("declines mul", lambda: Declines([1]) * 2)
attempt("declines tuple", lambda: DeclinesTuple((1,)) + (2,))
print({1}.__or__(1), {1}.__and__([1]), frozenset({1}).__xor__(1))


def set_inplace(value, other):
    value |= other
    return value


attempt("set ior", lambda: set_inplace({1}, 2))
attempt("set ior rmul", lambda: set_inplace({1}, Rmul()))
identity = [1]
same = inplace(identity, [2])
print(same is identity)
raw = bytearray(b"a")
print(inplace(raw, b"b") is raw, raw)
print(sum([[1], [2]], []), sum([Unrelated()], 1))
try:
    print(sum(["a"], 1))
except TypeError as e:
    print(e)
for symbol, left, right in (("-", 1, "a"), ("/", "a", 1), ("//", [], 1), ("%", None, 1), ("**", {}, 1), ("<<", 1.5, 1),
                            (">>", "a", 2), ("&", 1.5, 1), ("|", "a", 1), ("^", None, 1), ("@", 1, 2)):
    try:
        print(eval(f"left {symbol} right"))
    except TypeError as e:
        print(e)
    namespace = {"left": left, "right": right}
    try:
        exec(f"left {symbol}= right", namespace)
    except TypeError as e:
        print(e)
print(1.5 - True, 2 ** 0.5, 2 * 1.5, 1 + 2j, 1.5 * 2j, True + True, 3 % 2.5, divmod(7, 2.0), "%d" % 3, b"%d" % 3, [1] * True)
print(None | int, int | None, list[int] | None)


class Every:
    pass


class Right:
    pass


names = (("add", "+"), ("sub", "-"), ("mul", "*"), ("truediv", "/"), ("floordiv", "//"), ("mod", "%"), ("pow", "**"),
         ("lshift", "<<"), ("rshift", ">>"), ("and", "&"), ("or", "|"), ("xor", "^"), ("matmul", "@"))
for name, symbol in names:
    setattr(Every, f"__{name}__", lambda self, other, n=name: f"left {n}")
    setattr(Every, f"__i{name}__", lambda self, other, n=name: f"in place {n}")
    setattr(Right, f"__r{name}__", lambda self, other, n=name: f"right {n}")
    print(symbol, eval(f"Every() {symbol} 1"), eval(f"1 {symbol} Right()"))
    namespace = {"x": Every(), "Right": Right}
    exec(f"x {symbol}= 1", namespace)
    print(namespace["x"])
    namespace = {"x": 1, "Right": Right}
    exec(f"x {symbol}= Right()", namespace)
    print(namespace["x"])
