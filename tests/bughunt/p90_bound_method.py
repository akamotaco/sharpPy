# P0-54 acceptance. A bound method compares by (function ==, receiver is).
# Anchors: Objects/classobject.c:245 method_richcompare and :299 method_hash.
class O:
    def f(self):
        return 1

    def g(self):
        return 1

    @staticmethod
    def s():
        return 2

    @classmethod
    def c(cls):
        return 3


class Weird(O):
    def __eq__(self, other):
        return True

    def __hash__(self):
        return 7


o = O()
p = O()
w = Weird()


def show(label, fn):
    try:
        print("%-34s %r" % (label, fn()))
    except Exception as exc:
        print("%-34s %s: %s" % (label, type(exc).__name__, exc))


show("o.f == o.f", lambda: o.f == o.f)
show("o.f != o.f", lambda: o.f != o.f)
show("o.f == p.f", lambda: o.f == p.f)
show("o.f == o.g", lambda: o.f == o.g)
show("o.f == O.f", lambda: o.f == O.f)
show("O.f == O.f", lambda: O.f == O.f)
show("o.f == o.s", lambda: o.f == o.s)
show("o.s == o.s", lambda: o.s == o.s)
show("o.c == o.c", lambda: o.c == o.c)
show("o.c == O.c", lambda: o.c == O.c)
show("o.f == 5", lambda: o.f == 5)
show("o.f == None", lambda: o.f == None)
# The receiver is compared by identity, so its own __eq__ never enters (:261).
show("w.f == w.f", lambda: w.f == w.f)
show("o.f == w.f", lambda: o.f == w.f)
show("o.f == Weird().f", lambda: o.f == Weird().f)

show("hash(o.f) == hash(o.f)", lambda: hash(o.f) == hash(o.f))
show("hash(o.f) != hash(p.f)", lambda: hash(o.f) != hash(p.f))
show("hash(o.f) == hash(o.g)", lambda: hash(o.f) == hash(o.g))
show("{o.f: 1}[o.f]", lambda: {o.f: 1}[o.f])
show("len({o.f, o.f})", lambda: len({o.f, o.f}))
show("len({o.f, p.f})", lambda: len({o.f, p.f}))
show("o.f in {o.f}", lambda: o.f in {o.f})
show("o.f in {p.f}", lambda: o.f in {p.f})

# The attributes the comparison reads.
show("o.f.__func__ is O.f", lambda: o.f.__func__ is O.f)
show("o.f.__self__ is o", lambda: o.f.__self__ is o)
show("o.c.__self__ is O", lambda: o.c.__self__ is O)

# The builtin side of the same rule (Objects/methodobject.c:292 meth_richcompare, :316 meth_hash):
# the receiver by identity and the wrapped function, so the same attribute twice is one key.
l = []


def show_builtin(label, fn):
    try:
        print("%-34s %r" % (label, fn()))
    except Exception as exc:
        print("%-34s %s: %s" % (label, type(exc).__name__, exc))


show_builtin("l.append == l.append", lambda: l.append == l.append)
show_builtin("l.append == l.extend", lambda: l.append == l.extend)
show_builtin("l.append == print", lambda: l.append == print)
show_builtin("(1).bit_length == (1).bit_length", lambda: (1).bit_length == (1).bit_length)
show_builtin("hash(l.append) == hash(l.append)", lambda: hash(l.append) == hash(l.append))
show_builtin("{l.append: 1}[l.append]", lambda: {l.append: 1}[l.append])
show_builtin("l.append in {l.append}", lambda: l.append in {l.append})
show_builtin("len({l.append, l.append})", lambda: len({l.append, l.append}))
show_builtin("str.join == str.join", lambda: str.join == str.join)
show_builtin("{}.keys == {}.keys", lambda: {}.keys == {}.keys)

# The L2 leftover: atexit compares with the same rule, so a bound method can be unregistered.
import atexit

seen = []


class Keep:
    def bye(self):
        seen.append("ran")


k = Keep()
atexit.register(k.bye)
atexit.unregister(k.bye)
atexit.register(Keep().bye)
atexit._clear()
print("registered callbacks left:", atexit._ncallbacks())
print("callback ran during this process:", seen)
