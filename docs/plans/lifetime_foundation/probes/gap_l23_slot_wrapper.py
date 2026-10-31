def show(label, fn):
    try:
        print("%-34s %s" % (label, ascii(fn())))
    except Exception as exc:
        print("%-34s %s: %s" % (label, type(exc).__name__, exc))


class H(int):
    pass


h = H(3)
n = 3
l = []
show("h.__str__ == h.__str__", lambda: h.__str__ == h.__str__)
show("type(l.__len__)", lambda: type(l.__len__).__name__)
show("type(n.__add__)", lambda: type(n.__add__).__name__)
show("n.__add__ == n.__add__", lambda: n.__add__ == n.__add__)
s = "ab"
show("s.upper == \"ab\".upper", lambda: s.upper == "ab".upper)
show("s is \"ab\"", lambda: s is "ab")
show("t = \"a\"; t += \"b\"; t is s", lambda: (lambda: (lambda t: t is s)(s))())
show("(\"a\"+\"b\") is s", lambda: ("a" + "b") is s)
show("id(s) == id(\"ab\")", lambda: id(s) == id("ab"))
