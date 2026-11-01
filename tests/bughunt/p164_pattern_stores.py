# compiler_pattern_subpattern: every capture is a store on the stack, bound only on success.
class Point:
    __match_args__ = ("x", "y")

    def __init__(self, x, y):
        self.x = x
        self.y = y


def atomic(subject):
    a = b = rest = "unbound"
    match subject:
        case [a, b, 1]:
            pass
        case {"k": a, "l": 1, **rest}:
            pass
        case Point(a, 1):
            pass
        case [a, *rest, 1]:
            pass
    return a, b, rest


for subject in ([1, 2, 3], {"k": 1, "l": 2}, Point(1, 2), [1, 2, 3, 4], [1, 2, 1], {"k": 1, "l": 1, "m": 2}, Point(5, 1)):
    print(atomic(subject))


def alternatives(x):
    match x:
        case [z] | [1, (0 | 1 as z)] | (2 as z):
            return "a", z
        case [q, (0 | 1) as r] | {"q": q, "r": r}:
            return "b", q, r
        case Point(u, v) | Point(x=v, y=[u]):
            return "c", u, v
        case (m, [n, *o]) | (n, m, *o):
            return "d", m, n, o
    return None


for x in ([5], [1, 0], 2, [7, 1], {"q": 1, "r": 2}, Point(1, 2), (1, [2, 3, 4]), (1, 2, 3, 4)):
    print(alternatives(x))


def captured(x):
    match x:
        case [a, *b] if a:
            return lambda: (a, b)
        case {"a": a, **b}:
            return lambda: (a, b)
    return lambda: None


print(captured([1, 2])(), captured({"a": 1, "c": 2})(), captured([0])())
match [1, [2, [3, {"x": (4, 5)}]]]:
    case [a, [b, [c, {"x": (d, e)}]]]:
        print(a, b, c, d, e)


class Body:
    match (1, 2):
        case (p, q):
            pass


print(Body.p, Body.q)
for text in ("x", "_", "x as y", "x | 1", "_ as y", "1 | x | 2", "(x | y)"):
    try:
        compile(f"match 1:\n    case {text}:\n        pass\n    case 2:\n        pass\n", "<>", "exec")
        print("compiled", text)
    except SyntaxError as e:
        print(text, e.msg)
for text in ("[x, x]", "{'a': x, 'b': x}", "{'a': x, **x}", "Point(x, y=x)", "[x, *x]", "(x | [x, y])", "x as x", "[x] | [y]"):
    try:
        compile(f"match 1:\n    case {text}:\n        pass\n", "<>", "exec")
        print("compiled", text)
    except SyntaxError as e:
        print(text, e.msg)
print(ascii(f"\N{GREEK CAPITAL LETTER DELTA}"), ascii(f"{2}\N{BULLET}{3}"), f"\N{DIGIT ONE}{{x}}", rf"\N{1 + 1}")
print(ascii("\N{bullet} \N{LINE FEED} \N{HANGUL SYLLABLE GA} \N{HANGUL SYLLABLE HIH} \N{CJK UNIFIED IDEOGRAPH-4E00} \N{CJK UNIFIED IDEOGRAPH-20000} \N{GRINNING FACE} \N{LATIN CAPITAL LETTER GHA}"))
print(b"\N{BULLET}", r"\N{BULLET}", "\N{DIGIT ONE}" * 2)
for text in (r'"\N{NOT A NAME}"', r'"\N"', r'"\N{"', r'"\N{}"', r'"\N{hangul syllable ga}"', r'"\N{CJK UNIFIED IDEOGRAPH-1234}"', r'"\N{KEYCAP NUMBER SIGN}"', r'f"\N{NOT A NAME}"'):
    try:
        print(ascii(eval(text)))
    except SyntaxError as e:
        print(e.msg)
