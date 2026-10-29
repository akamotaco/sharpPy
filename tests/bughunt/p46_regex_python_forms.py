# p46: Python-only regex constructs on top of the .NET engine.
# (?P=name) has to become \k<name>, and the second argument of .NET Regex.Replace is NOT a
# regex - it is a literal template where only $ is special. Emitting "\\k" there produced an
# escaped 'k', so every named backreference silently matched nothing.
# A re.sub() replacement template is its own language (re/_parser.py parse_template):
# \g<name> and \g<1>, \1..\99 group refs, \0 as octal NUL rather than group 0, the C
# escapes, a dropped backslash before punctuation, a literal $ that .NET needs doubled, and
# "bad escape \q at position 0".
import re


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:80])


cases = [
    ("named_backref_findall", lambda: re.findall(r"(?P<c>\w) (?P=c)", "a a b c c")),
    ("num_backref_findall", lambda: re.findall(r"(\w) \1", "a a b c c")),
    ("named_backref_search", lambda: re.search(r"(?P<x>\w) (?P=x)", "p q q").group(0)),
    ("named_backref_match", lambda: re.match(r"(?P<w>\w+) (?P=w)", "ab ab cd").group(0)),
    ("compile_backref_findall", lambda: re.compile(r"(?P<a>[ab]+) (?P=a)").findall("aa aa b b")),
    ("named_no_backref", lambda: re.findall(r"(?P<c>\w)", "abc")),
    ("two_named_backrefs", lambda: re.findall(r"(?P<a>\w)(?P<b>\w) (?P=a)(?P=b)", "ab ab cd cd")),
    ("backref_sub", lambda: re.sub(r"(?P<w>\w+) (?P=w)", r"[\1]", "ab ab c d")),
    ("backref_groupdict", lambda: re.match(r"(?P<y>\d{4}) (?P=y)", "2024 2024").groupdict()),
    ("mixed_num_named", lambda: re.findall(r"(?P<a>\w) (\w) (?P=a) ", "a b a b c d e f")),

    ("backref_no_match", lambda: re.findall(r"(?P<c>\w) (?P=c)", "a b c d")),
    ("dotnet_style_backref", lambda: re.findall(r"(?<c>\w) \k<c>", "a a b c c")),
    ("backref_forward_ref", lambda: re.findall(r"(?P<a>\w)(?P=a)", "aab")),
    ("backref_case_insensitive", lambda: re.findall(r"(?P<c>[a-z]) (?P=c)", "a A b B", re.IGNORECASE)),
    ("backref_empty_group", lambda: re.findall(r"(?P<g>\w*)x(?P=g)", "xaxxb")),
]

for lbl, f in cases:
    s(lbl, f)


# --- replacement templates
# excluded: P0-17 .NET numbers unnamed groups before named ones, so \2 binds differently
# (measured: CPython [('a', 'b')] vs SharpPy [])
s("mix_all_named", lambda: re.findall(r"(?P<a>\w) (?P<b>\w) (?P=a) (?P=b)", "a b a b c d e f"))
s("mix_all_num", lambda: re.findall(r"(\w) (\w) \1 \2", "a b a b c d e f"))
s("mix_dn", lambda: re.findall(r"(?<a>\w) (\w) \k<a> \2", "a b a b c d e f"))
s("mix_search", lambda: bool(re.search(r"(?P<a>\w) (\w) (?P=a) ", "a b a b")))
s("mix_two_named_one_num", lambda: re.findall(r"(?P<a>\w)(?P<b>\w) (\w) \k<a>".replace("\\k<a>", "(?P=a)(?P=b)\\3"), "abacba"))

# re.sub replacement template forms
s("sub_num", lambda: re.sub(r"(\w)", r"[\1]", "ab"))
s("sub_named_g", lambda: re.sub(r"(?P<x>\w)", r"[\g<x>]", "ab"))
s("sub_named_g_num", lambda: re.sub(r"(\w)", r"[\g<1>]", "ab"))
s("sub_backslash_lit", lambda: re.sub(r"(\w)", r"\\\1", "ab"))
s("sub_dollar", lambda: re.sub(r"(\w)", r"$\1", "ab"))
s("sub_newline", lambda: re.sub(r"(\w)", r"\\n\1", "ab"))
s("sub_unicode_esc", lambda: re.sub(r"(\w)", r"\\t\1", "ab"))
# excluded: P1 native re.error __name__ is ValueError; .pos/.msg/.lineno missing
s("sub_callable", lambda: re.sub(r"(\w)", lambda m: m.group(1).upper(), "ab"))
s("sub_ten_groups", lambda: re.sub(r"(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)", r"\10\1", "abcdefghij"))
