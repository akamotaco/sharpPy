import re


def g(m):
    return None if m is None else (m.group(0), m.span(), m.groups())


print(g(re.match("b", "ab")), g(re.match(r"if\s+", "a if x")), g(re.match("a", "ab")), g(re.match("", "ab")))
print(g(re.compile("b").match("ab")), g(re.compile("b").match("ab", 1)), g(re.compile("b").match("abc", 0, 1)))
print(g(re.match("a|ab", "ab")), g(re.fullmatch("a|ab", "ab")), g(re.fullmatch("b", "ab")), g(re.fullmatch("a*", "aaa")))
print(g(re.compile("b").fullmatch("abc", 1, 2)), g(re.compile("b+").fullmatch("abb", 1)), g(re.compile("b").fullmatch("abc", 1)))
print(g(re.search("b", "abcb")), g(re.compile("b").search("abcb", 2)), g(re.compile("b").search("abcb", 2, 3)), g(re.compile("b").search("abcb", 0, 1)))
print(g(re.compile("^b").search("ab", 1)), g(re.compile("^b").match("ab", 1)), g(re.compile("^b", re.M).search("a\nb", 2)))
print(g(re.compile(r"(?<=a)b").match("ab", 1)), g(re.compile(r"\bb").search("ab", 1)), g(re.compile("b$").match("ab", 1)), g(re.compile("a$").match("ab", 0, 1)))
print(g(re.match(r"(?i)HELLO", "hello")), g(re.match("(a)|(b)", "b")), g(re.match(r"(?P<x>\d+)-(?P<y>\d+)", "12-34 z")), g(re.match(r"\s*", "  x")))
print(g(re.match(r"""
    (\d+)  # digits
    \s*    # space
    """, "12 x", re.X)), g(re.compile("a", re.I).match("A")), g(re.match(".", "\n")), g(re.match(".", "\n", re.S)))
print(g(re.compile("b").match("ab", 5)), g(re.compile("").match("ab", 5)), g(re.compile("b").match("ab", -3)), g(re.compile("a").search("ab", 1, 0)))
print(re.findall("a", "aba"), re.sub("^a", "x", "aa"), re.split("b", "abc"), [m.span() for m in re.finditer("a", "aba")])
m = re.match(r"(\w+) (\w+)", "hello world again")
print(m[0], m[1], m[2], m.group(1, 2), m.start(2), m.end(1), m.string, m.re.pattern, m.pos, m.endpos, m.lastindex)
print(bool(re.match(r"\d", "a1")), re.match(r"if\s+(.+)", "go if you can"), re.match(r"!\s*(.+)", "wow! yes"))
