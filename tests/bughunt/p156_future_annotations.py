"""docstring first"""
from __future__ import annotations

import collections.abc
import dataclasses
import typing
from dataclasses import dataclass, field
from typing import ClassVar, Optional


class Node:
    parent: Node | None = None
    kids: list[Node]
    table: dict[str, tuple[int, ...]]
    cb: collections.abc.Callable[[int, str], None]
    lit: typing.Literal["a", 1, None]
    count: ClassVar[int] = 0

    def link(self, other: Node, *rest: Node, flag: "bool" = False, **kw: dict[str, Node]) -> Optional[Node]:
        return other

    @classmethod
    def make(cls, n: int = 3) -> Node:
        return cls()


@dataclass(order=True)
class Item:
    name: str
    qty: int = 1
    tags: list[str] = field(default_factory=list)
    total: ClassVar[int] = 0
    alt: dataclasses.InitVar[int] = 0
    next: Item | None = None


async def go(a: Node) -> list[Node]:
    return [a]


x: Undefined = 1
y: list[Undefined]
print(Node.__annotations__)
print(Node.link.__annotations__, Node.make.__annotations__, go.__annotations__)
print(__annotations__)
print(Item("a"), [f.name for f in dataclasses.fields(Item)], Item("a") < Item("b"), Item.__annotations__["next"])
print(typing.get_type_hints(Node.make), typing.get_type_hints(Node.link)["other"].__name__)


def local():
    z: Undefined = 2
    def inner(q: Other) -> Another:
        return q
    return z, inner.__annotations__


print(local(), (lambda: None).__annotations__)
exec("from __future__ import annotations\ndef f(a: Nope): pass\nprint(f.__annotations__)")
