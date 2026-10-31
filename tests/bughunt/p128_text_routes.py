# Source text as the tokenizer must see it, and regular expressions as _parser.py reads them.
import re

a = 1
print(f"{{x}}", rf"{{x}}", fr"{{{a}}}", rf"""{{y}}""", f"""{{z}}""", rf"\{{", fr"a{{b}}c", f"{a}{{", f"}}")
print(repr(f"""a{{
}}b"""), f"{a!r:>3}{{")
print(.5, [.25], -.5, .5e2, .5j, 1 if True else.5)

ns = {}
exec(compile("s = '''a\r\nb'''\nt = 'c'\r\nu = 1\rv = 2\n", "x", "exec"), ns)
print(repr(ns["s"]), ns["t"], ns["u"], ns["v"])

p = re.compile(
    r"""
    (?P<a> [a-z]+ )   # letters
    [ ]               # one space, kept
    (?P<b> \# \d+ )   # an escaped hash
    """,
    re.VERBOSE,
)
print(p.findall("ab #12"), p.flags, re.compile("x").flags, re.compile("x", re.I).flags, p.pattern[:5] == "\n    ")
print(re.findall(r"(?a:\w+)", "ab cd"), re.findall(r"(?i:A)b", "ab Ab"), re.findall(r"(?x: a  b )", "ab"))
print(re.compile(r"(\s+)").split("a b  c"), re.findall(r"[ #]", "a #"), re.compile("[ ] a", re.X).findall(" a"))
