# p31: re named groups (?P<name>) + (?P=name) + findall single-group shape.
import re


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


show("named-group", lambda: re.match(r"(?P<y>\d{4})-(?P<m>\d{2})", "2024-07").group("y"))
show("named-month", lambda: re.match(r"(?P<y>\d{4})-(?P<m>\d{2})", "2024-07").group("m"))
show("groupdict", lambda: re.match(r"(?P<y>\d{4})", "2024").groupdict())
show("pos-then-named", lambda: re.match(r"(\d)(?P<b>\w)", "1a").group("b"))
show("named-in-compile", lambda: re.compile(r"(?P<x>[a-z]+)").match("hi").group("x"))
show("dotnet-syntax-works", lambda: re.match(r"(?<y>\d{4})", "2024").group("y"))
# findall return shape: 0 groups -> whole match, 1 group -> string, 2+ -> tuples.
show("findall-0grp", lambda: re.findall(r"\d+", "a1b22c333"))
show("findall-1grp", lambda: re.findall(r"(\w) \1", "a a b c c"))
show("findall-2grp", lambda: re.findall(r"(\w)(\d)", "a1b2"))
show("findall-noncap", lambda: re.findall(r"(?:ab)+", "ababc"))
show("findall-named-1", lambda: re.findall(r"(?P<n>\d+)", "a11 b22"))
show("finditer-1grp", lambda: [m.group(1) for m in re.finditer(r"(\w)\s", "a b c ")])
