def show(label, fn):
    try:
        print(f"{label!r}: {fn()!r}")
    except BaseException as e:
        print(f"{label!r}: EXC {type(e).__name__}: {e}")

show("slice.assign.step.mismatch", lambda: (lambda L: (L.__setitem__(slice(None, None, 2), [9, 9]), L)[1])([0, 1, 2, 3]))
show("slice.assign.step.len.err", lambda: (lambda L: L.__setitem__(slice(None, None, 2), [9, 9, 9]))([0, 1, 2, 3]))
show("slice.del.step", lambda: (lambda L: (L.__delitem__(slice(None, None, -1)), L)[1])([1, 2, 3]))
show("slice.neg.step.assign", lambda: (lambda L: (L.__setitem__(slice(None, None, -1), [10, 20, 30]), L)[1])([1, 2, 3]))
show("slice.del.oob", lambda: (lambda L: (L.__delitem__(slice(5, 9)), L)[1])([1, 2, 3]))
show("list.add.tuple", lambda: [1] + (2,))
show("list.imul.iterator", lambda: (lambda L: L.__imul__(iter([1, 2])))([0]))
show("list.sort.stability", lambda: sorted(map(list, zip([2, 1, 1, 3], "bcad"))))
show("list.sort.key.none", lambda: sorted([1, "a"], key=lambda x: None) if False else sorted([3, 1, 2], key=None))
show("list.sort.cmp.deleted", lambda: (lambda L: (L.sort(), L[0]))([3, 1, 2]))
def sort_explodes(x):
    if x == 2:
        raise ValueError("boom")
    return x
def sort_mid_exception():
    L = [3, 2, 1]
    try:
        L.sort(key=sort_explodes)
    except ValueError as e:
        print("  raised", e)
    return L
show("list.sort.key.exception.state", sort_mid_exception)
show("sorted.tuple", lambda: type(sorted((2, 1))) is list)
show("tuple.mul.zero", lambda: (1, 2) * 0)
show("tuple.add.list", lambda: (1,) + [2])
show("tuple.index.default", lambda: (1, 2, 3).index(4, 0, 100) if False else (1, 2).index(3, 0, 1))
show("list.mul.neg", lambda: [1] * -2)
show("list.contains.nan", lambda: [float('nan') in ([float('nan')] * 1), (lambda x: x in [x])(float('nan'))])
show("in.getitem.fallback", lambda: 5 in type("S", (), {"__getitem__": lambda s, i: ["a", "b", "c"][i]})())
show("set.literal.order", lambda: sorted({3, 1, 2}))
show("set.from.dup", lambda: len(set([1, 1.0, True])))
show("set.ops.types", lambda: [type({1} | {2}) is set, type({1} & {2}) is set, type({1} - {2}) is set, type({1} ^ {2}) is set])
show("set.op.operand.mixed", lambda: [list({1, 2} | [2, 3]), type({1, 2} | [2, 3]) is set])
show("set.update.chain", lambda: (lambda s: (s.update([1], {2}), sorted(s))[1])({0}))
show("set.discard.absent", lambda: (lambda s: (s.discard(99), len(s))[1])({1}))
show("set.pop.empty", lambda: set().pop())
show("frozenset.hash.subset", lambda: {frozenset([1]): "fs"}[frozenset([1])])
show("set.of.list", lambda: {[]})
show("set.add.unhashable.tuple", lambda: {(1, [2])})
show("dict.fromkeys.default", lambda: dict.fromkeys([1, 2]))
show("dict.fromkeys.shared", lambda: (lambda d: (d[1].append(9), d)[1])(dict.fromkeys([1, 2], [])))
show("list.clear", lambda: (lambda L: (L.clear(), L)[1])([1]))
show("list.copy.shallow", lambda: (lambda L, C: (C[0].append(9), C)[1])([[]], [[]].copy() if False else list([[]])))
show("enumerate.start", lambda: list(enumerate("ab", start=1)))
show("reversed.range", lambda: list(reversed(range(3))))
show("reversed.nonseq", lambda: list(reversed({1, 2})))
show("reversed.getlen.getitem", lambda: list(reversed(type("S", (), {"__len__": lambda s: 3, "__getitem__": lambda s, i: i * 10})())))
show("zip.strict.short", lambda: list(zip([1, 2], [1], strict=True)))
show("zip.strict.ok", lambda: list(zip([1], [2], strict=True)))
show("map.lazy", lambda: (lambda it: (next(it), next(it)))(map(print, "ab")))
show("iter.sentinel", lambda: list(iter(lambda: "x".find("y"), -1)))
_seq = iter([1, 2, 0, 5])
show("iter.sentinel.stop.early", lambda: list(iter(_seq.__next__, 0)))
show("next.default", lambda: next(iter([]), "dflt"))
show("next.no.default", lambda: next(iter([])))
