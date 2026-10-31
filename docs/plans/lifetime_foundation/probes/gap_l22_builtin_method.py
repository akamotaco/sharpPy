import math

l = []


def show(label, fn):
    try:
        print("%-36s %s" % (label, ascii(fn())))
    except Exception as exc:
        print("%-36s %s: %s" % (label, type(exc).__name__, exc))


show("math.sqrt == math.sqrt", lambda: math.sqrt == math.sqrt)
show("sqrt bound twice equal", lambda: (math.sqrt, math.sqrt)[0] == math.sqrt)
show("(1).bit_length == (1).bit_length", lambda: (1).bit_length == (1).bit_length)
show("l.append == l.append", lambda: l.append == l.append)
show("l.append == l.extend", lambda: l.append == l.extend)
show("l.append == print", lambda: l.append == print)
show("hash(l.append) == hash(l.append)", lambda: hash(l.append) == hash(l.append))
show("{l.append: 1}[l.append]", lambda: {l.append: 1}[l.append])
show("l.append in {l.append}", lambda: l.append in {l.append})
show("type(l.append)", lambda: type(l.append).__name__)
show("type(math.sqrt)", lambda: type(math.sqrt).__name__)
show("type(str.join)", lambda: type(str.join).__name__)
show("str.join == str.join", lambda: str.join == str.join)
show("type([].append.__self__)", lambda: type([].append.__self__).__name__)
