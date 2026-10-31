# Q2 acceptance. Does anything inside the VM keep a dropped object alive?
# Every scenario prints 1 (finalized by the end of gc.collect()), except the two marked below.
#
# Measured on 2026-10-31 before Q2, one process per scenario, default tiering and
# DOTNET_TieredCompilation=0, using the then-existing bug as the observer (a collected object lost its
# __del__): 29 dropped scenarios collected, control held. So a nonzero number here after Q2 means a
# real retention bug or a missing drain, and it is not "timing".
import gc

fired = []


class T:
    def __del__(self):
        fired.append(1)


class H:
    pass


def c():
    gc.collect()
    gc.collect()


def s_control_kept():  # prints 0: still reachable from a global
    global k
    k = T()


def s_global_del():
    global o
    o = T()
    del o


def s_local_del():
    o = T()
    del o
    c()


def s_return_drop():
    def f():
        o = T()
        return 1

    f()


def s_call_arg():
    def f(x):
        return x

    o = T()
    f(o)
    del o


def s_call_arg_returned_ignored():
    def f(x):
        return x

    f(T())


def s_builtin_temp():
    o = T()
    repr(o)
    [o]
    (o,)
    {1: o}
    del o


def s_bound_method():
    class M(T):
        def m(self):
            pass

    o = M()
    w = o.m
    w()
    del w, o


def s_attr():
    h = H()
    h.o = T()
    h.o
    del h.o


def s_attr_read_cache():
    h = H()
    h.o = T()
    for i in range(5):
        h.o
    h.o = None


def s_for_target():
    for x in [T()]:
        pass
    del x


def s_comprehension():
    ys = [y for y in [T()]]
    del ys


def s_genexpr():
    sum(1 for y in [T()])


def s_handled_exc_frame():
    def f():
        o = T()
        raise ValueError("x")

    try:
        f()
    except ValueError:
        pass


def s_handled_exc_bound():
    def f():
        o = T()
        raise ValueError("x")

    try:
        f()
    except ValueError as e:
        pass


def s_exc_arg():
    try:
        raise ValueError(T())
    except ValueError:
        pass


def s_closed_gen():
    def g():
        o = T()
        yield 1

    i = g()
    next(i)
    i.close()
    del i


def s_suspended_gen():
    def g():
        o = T()
        yield 1

    i = g()
    next(i)
    del i


def s_exhausted_gen():
    def g():
        o = T()
        yield 1

    list(g())


def s_closure():
    o = T()
    fn = lambda: o
    fn()
    del fn
    del o


def s_dict_item():
    d = {"k": T()}
    del d["k"]


def s_list_pop():
    l = [T()]
    l.pop()


def s_cycle():
    h = H()
    h.me = h
    h.o = T()


def s_class_attr():
    class A:
        pass

    A.o = T()
    del A.o


def s_class_dropped():
    class A:
        pass

    A.o = T()


def s_user_subclass_instance():
    class L(list):
        pass

    l = L()
    l.append(T())
    l.clear()


def s_kwargs():
    def f(**kw):
        pass

    f(a=T())


def s_star_args():
    def f(*a):
        pass

    f(*[T()])


def s_method_on_instance():
    class A:
        def m(self, x):
            return None

    A().m(T())


def s_super_call():
    class A:
        def __init__(self, x):
            pass

    class B(A):
        def __init__(self, x):
            super().__init__(x)

    B(T())


names = sorted(n for n in globals() if n.startswith("s_"))
for name in names:
    fired.clear()
    globals()[name]()
    c()
    print(name, len(fired))
