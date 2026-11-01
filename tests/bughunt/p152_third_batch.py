import abc
import enum
import itertools
import math
from contextlib import ExitStack
from typing import Optional, TypeVar


class Color(enum.Enum):
    RED = 1
    GREEN = 2


class State:
    IDLE = "idle"


def value_pattern(x):
    match x:
        case Color.RED:
            return "red"
        case State.IDLE:
            return "idle"
        case math.pi:
            return "pi"
        case -1:
            return "neg"
        case 1 + 2j:
            return "complex"
        case _:
            return "other"


print(value_pattern(Color.RED), value_pattern(Color.GREEN), value_pattern("idle"), value_pattern(math.pi), value_pattern(-1), value_pattern(1 + 2j))


def retry(times, exceptions=(ValueError,)):
    def deco(fn):
        def wrapper(*a):
            for attempt in range(times):
                try:
                    return fn(*a)
                except exceptions as e:
                    last = e
            return repr(last)
        return wrapper
    return deco


@retry(2)
def fail():
    raise ValueError("x")


print(fail())


class Grid:
    def __init__(self):
        self.width = 2
        self.height = 2
        self.rows = [[1, 2], [3, 4]]

    def render(self):
        table = {1: "a", 2: "b", 3: "c", 4: "d"}
        other = 0
        return ["".join(table.get(self.rows[y][x], "?") for x in range(self.width)) for y in range(self.height)]


print(Grid().render())


class Meta(type):
    def __getitem__(cls, key):
        return f"{cls.__name__}:{key}"


class Keyed(metaclass=Meta):
    def __class_getitem__(cls, key):
        return "class_getitem"


class Plain:
    def __class_getitem__(cls, key):
        return "class_getitem"


print(Keyed["a"], Plain["a"], Color["RED"], Color["GREEN"].value, list[int])


class Shape(abc.ABC):
    @property
    @abc.abstractmethod
    def name(self):
        pass

    @abc.abstractmethod
    def area(self):
        pass


class Half(Shape):
    def area(self):
        return 1


class Full(Half):
    @property
    def name(self):
        return "full"


print(sorted(Shape.__abstractmethods__), sorted(Half.__abstractmethods__), Full().name)
for cls in (Shape, Half):
    try:
        cls()
    except TypeError as e:
        print(e)


class Count:
    def __init__(self):
        self.n = 0

    def __iter__(self):
        return self

    def __next__(self):
        self.n += 1
        return self.n


print(list(itertools.islice(Count(), 3)), list(itertools.takewhile(lambda v: v < 3, Count())), list(itertools.dropwhile(lambda v: v < 2, iter([1, 2, 1]))))
print(list(itertools.compress(iter("ab"), iter([1, 0]))), list(itertools.filterfalse(None, iter([0, 1]))), list(zip(iter([1]), Count())))
T = TypeVar("T")
print(T | None, Optional[int], None | T, int | str | None)
with ExitStack() as stack:
    stack.callback(print, "exit")
    print("in")


class Pair:
    __match_args__ = ("a",)

    def __init__(self):
        self.a = 1


match Pair():
    case Pair(a=v) if v > 5:
        print("big")
    case Pair(v):
        print("small", v)
