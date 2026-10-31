# L2.5 route: args is a getset, not a raw member. BaseException_set_args (Objects/exceptions.c:261)
# stores PySequence_Tuple(val), and the getter hands back the stored tuple. L2.4 stored the value as is,
# then ArgsOf read a non-tuple as empty, so the assigned value disappeared.
class E(Exception):
    pass


class S(SystemExit):
    pass


for k in (E, S, ValueError, SystemExit):
    print(k.__name__)
    e = k("a")
    e.args = [1, 2]
    print(e.args, type(e.args).__name__, repr(e), str(e))
    e.args = "xy"
    print(e.args)
    e.args = (i for i in range(2))
    print(e.args)
    e.args = ()
    print(e.args, repr(e), repr(str(e)))
    before = e.args
    try:
        e.args = 5
    except TypeError as x:
        print(x)
    print(e.args is before)
    e.args = (1, 2)
    print(e.args is e.args)
    t = (3, 4)
    e.args = t
    print(e.args is t)
    object.__setattr__(e, "args", [5])
    print(e.args, type(e.args).__name__)
    print(k("p", "q").args is not None)
