# P0-21: object_new / object_init argument rules.
# Measured against CPython 3.12.15 (Objects/typeobject.c:5416-5457). Two predicates decide the
# wording: whether tp_new is object_new, and whether tp_init is object_init.
# Repr is normalised to "<instance>" so the address text, which is a separate display gap, does not
# leak into this comparison.


def s(label, fn):
    try:
        r = fn()
        if isinstance(r, (A, B, C, D, E, F, I2, K, L2)):
            print(label, "<instance>")
        else:
            print(label, repr(r))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__, str(exc)[:78])


class A:
    pass


class B:
    def __init__(self):
        pass


class C:
    def __init__(self, x):
        self.x = x


class D:
    def __new__(cls):
        return object.__new__(D)


class E:
    def __new__(cls, x):
        self = object.__new__(E)
        self.x = x
        return self


class F:
    def __init__(self, *args, **kwargs):
        pass


class I2:
    def __new__(cls, *args, **kwargs):
        return object.__new__(cls)


class K(D):
    pass


class L2(B):
    pass


# no __new__ and no __init__: object_new refuses the arguments
s("A_plain", lambda: A())
s("A_pos", lambda: A(1))
s("A_kw", lambda: A(x=1))
s("A_two", lambda: A(1, 2))

# an __init__ exists, so object_new falls through and the user function reports its own arity
s("B_pos", lambda: B(1))
s("B_kw", lambda: B(x=1))
s("C_missing", lambda: C())
s("C_two", lambda: C(1, 2))
s("C_kw_named", lambda: C(x=1))
s("C_ok", lambda: C(1).x)

# an explicit __new__ is a real slot and gets the arguments
s("D_pos", lambda: D(1))
s("D_kw", lambda: D(x=1))
s("E_pos", lambda: E(1).x)
s("E_pos2", lambda: E(1, 2))
s("F_any", lambda: F(1, 2, k=3))
s("I_new_varargs", lambda: I2(1, x=2))
s("K_inherits_new", lambda: K(3))

# inherited user __init__ keeps the same rules
s("L2_pos", lambda: L2(1))
s("L2_ok", lambda: L2())

# calling the object hooks directly
s("object_new_extra", lambda: object.__new__(A, 1))
s("object_new_kw", lambda: object.__new__(A, x=1))
s("object_new_plain", lambda: type(object.__new__(A)).__name__)
s("object_init_extra", lambda: object.__init__(A(), 1))
s("object_init_kw", lambda: object.__init__(A(), x=1))
s("object_init_ok", lambda: object.__init__(A()))
s("object_init_user_init", lambda: object.__init__(C(1), 2))
s("object_call", lambda: type(object()).__name__)
s("object_call_arg", lambda: object(1))

# a builtin base's own tp_new handles the arguments, so object_new is never reached
s("dict_subclass_kwargs", lambda: dict(type("MD", (dict,), {})(a=1)))
s("list_subclass_args", lambda: list(type("ML", (list,), {})([1, 2])))
s("int_subclass_value", lambda: int(type("MI", (int,), {})(7)))
s("int_subclass_bad", lambda: type("MI", (int,), {})(7, 8))
