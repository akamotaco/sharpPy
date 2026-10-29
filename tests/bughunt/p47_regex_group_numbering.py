# p47: capture-group numbering. CPython assigns group numbers by the position of '(' in
# re/_parser.py and _compiler.py resolves every reference to that number, so the matcher never
# re-derives numbering. The .NET engine numbers capture ids with the unnamed groups first, which
# is a different numbering: before the canonical-name rewrite, (?P<a>\w)(\w) reported the groups
# swapped (span(1) was (2, 3)) and (?P=a)  stopped matching altogether. Patterns are now
# rewritten so each capture group carries its CPython number in a canonical name, and group(),
# span(), start(), end(), groups(), groupdict(), findall() and replacement templates all resolve
# through it. A pattern the scanner does not model falls back to the plain translation instead
# of answering with wrong groups.

def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:70])

# Scanner edge cases for canonical renaming.
s("class_paren", lambda: re.findall(r"([(]\w)", "(a b"))
s("class_named_mix", lambda: re.findall(r"(?P<a>[)(]) (\w) (?P=a)", "( x ( y )"))
s("nested", lambda: re.findall(r"((?P<a>\w)(\d))", "a1 b2"))
s("nested_spans", lambda: re.match(r"((?P<a>\w)(\d))", "a1").span(2))
s("optional_group", lambda: re.match(r"(?P<a>a)(?P<b>b)?", "a").group("b"))
s("optional_group_num", lambda: re.match(r"(?P<a>a)(b)?", "a").group(2))
s("lookahead_inner", lambda: re.findall(r"(?=(\w))\w", "ab"))
s("lookbehind_named", lambda: re.findall(r"(?<=\w)(?P<x>\d)", "a1 b2"))
s("noncap_mix", lambda: re.findall(r"(?:\w)(?P<a>\w)(\w)", "abc"))
s("escaped_paren", lambda: re.findall(r"(?P<a>\()\)(\w)", "()(x"))
s("digits_ref10", lambda: re.match(
    r"(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k) \10 \11", "a b c d e f g h i j k j k").groups())
s("flag_scope", lambda: re.findall(r"(?i)(?P<a>ABC) (?P=a)", "abc abc"))
s("comment_ext", lambda: re.findall(r"(?#c)(?P<a>\w)", "ab"))
s("conditional", lambda: re.findall(r"(?P<a>x)?(?(a)Y|N)", "x N"))
s("pipe_named", lambda: re.match(r"(?P<a>a)|(?P<b>b)", "b").groupdict())
s("repeat_backref", lambda: re.findall(r"(?P<w>\w)\s(?P=w)+", "a aa b bb"))
s("split_named", lambda: re.split(r"(?P<sep>[,-])", "a,b-c"))
s("split_num", lambda: re.split(r"([,-])", "a,b-c"))
s("sub_named_mix", lambda: re.sub(r"(?P<a>\w) (\w)", r"\2\1", "ab cd"))
s("sub_name_mix", lambda: re.sub(r"(?P<a>\w) (\w)", r"\g<a>\g<2>", "ab cd"))
s("grouplist_names", lambda: re.match(r"(?P<a>a)(b)", "ab").groups())
s("start_end", lambda: (re.match(r"(?P<a>a)(b)", "ab").start("a"),
                        re.match(r"(?P<a>a)(b)", "ab").end(2)))
s("bad_name", lambda: re.match(r"(?P<a>a)", "a").group("zz"))
s("big_num", lambda: re.match(r"(?P<a>a)", "a").group(9))


# --- numbering spot checks
import re


s("no_backref_findall", lambda: re.findall(r"(?P<a>\w)(\w)", "ab"))
s("no_backref_two", lambda: re.findall(r"(?P<a>\w) (\w)", "a b"))
m = re.match(r"(?P<a>\w) (\w)", "a b")
s("g1", lambda: m.group(1))
s("g2", lambda: m.group(2))
s("gname", lambda: m.group("a"))
s("span1", lambda: m.span(1))
s("span2", lambda: m.span(2))
s("num_first", lambda: re.findall(r"(\w)(?P<a>\w)", "ab"))
m2 = re.match(r"(\w)(?P<a>\w)", "ab")
s("nf_g1", lambda: m2.group(1))
s("nf_gname", lambda: m2.group("a"))


# --- mixed named / unnamed references
import re


s("A_named_then_numref1", lambda: re.findall(r"(?P<a>\w) (\w) (?P=a) \1", "a b a a"))
s("B_named_then_numref2", lambda: re.findall(r"(?P<a>\w) (\w) (?P=a) \2", "a b a b"))
s("C_only_namedref", lambda: re.findall(r"(?P<a>\w) (\w) (?P=a)", "a b a"))
s("D_only_numref", lambda: re.findall(r"(?P<a>\w) (\w) \2", "a b b"))
s("E_num_named_num", lambda: re.findall(r"(\w) (?P<b>\w) \1 (?P=b)", "a b a b"))
s("F_named_two_named", lambda: re.findall(r"(?P<a>\w) (?P<b>\w) (?P=a)", "a b a"))
s("G_named_num_named", lambda: re.findall(r"(?P<a>\w) (\w) (?P=a) (?P=a)", "a b a a"))
s("H_backref_before_group2", lambda: re.findall(r"(?P<a>\w) (?P=a) (\w)", "a a b"))
s("I_three", lambda: re.findall(r"(?P<a>\w) (\w) (\w) (?P=a) \2 \3", "a b c a b c"))
