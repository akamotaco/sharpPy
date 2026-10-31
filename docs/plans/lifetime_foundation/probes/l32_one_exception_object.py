# do_raise raises the object it was given, so there is one exception object however often it is raised
# and whoever reads it (P0-70).
import copy
import sys


class E(Exception):
    pass


class S(SystemExit):
    pass


z = E("boom")
for kind in (E, Exception, BaseException):
    try:
        raise z
    except kind as e:
        print(e is z, type(e).__name__, repr(e), e.args, sys.exc_info()[1] is z, sys.exception() is z)

seen = []
for i in range(3):
    try:
        raise z
    except E as c:
        seen.append(c)
print(seen[0] is seen[1], len({id(x) for x in seen}))

z = E("a")
try:
    raise z
except E as e:
    e.mark = 1
    e.args = ("b",)
print(z.__dict__, z.args, z.__traceback__ is not None, z.__cause__, z.__context__, z.__suppress_context__)
print(E("x").__traceback__, E("x").__cause__, E("x").__context__, E("x").__suppress_context__)

z = E("n")
z.add_note("first")
try:
    raise z
except E as e:
    e.add_note("second")
print(z.__notes__)
v = ValueError(1)
v.add_note("x")
v.tag = 5
print(v.__notes__, v.tag, v.__dict__)
try:
    z.add_note(5)
except TypeError as x:
    print(x)
print(E("t").with_traceback(None).args)

c = E("c")
v = ValueError("v")
for k in (c, v):
    try:
        raise KeyError(1) from k
    except KeyError as x:
        print(x.__cause__ is k, x.__suppress_context__)
try:
    raise E("a") from v
except E as x:
    print(x.__cause__ is v)
for cause in (TypeError, E, None, 5, int):
    try:
        try:
            raise ValueError(1) from cause
        except ValueError as x:
            print(repr(x.__cause__), x.__suppress_context__)
    except TypeError as x:
        print(x)
try:
    try:
        raise c
    except E as a:
        raise ValueError(1)
except ValueError as b:
    print(b.__context__ is c)


class CM:
    def __enter__(self):
        return self

    def __exit__(self, t, v, tb):
        print(t.__name__, v is Z, tb is not None)
        return True


Z = E(1)
with CM():
    raise Z


def g():
    try:
        yield 1
    except E as e:
        print(e is Z)
        yield 2


x = g()
next(x)
print(x.throw(Z))

w = E(1)
try:
    try:
        raise w
    except E:
        raise
except E as e:
    print(e is w)

e = E(1, 2)
e.x = 1
print(E(1).__reduce__(), e.__reduce__())
print(copy.copy(e).args, copy.deepcopy(e).x, copy.copy(ValueError(3)).args, copy.copy(S(4)).code)
print(type(copy.copy(e)).__name__)

s = S(3)
try:
    raise s
except SystemExit as q:
    print(q is s, q.code)
