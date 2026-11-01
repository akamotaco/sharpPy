# starunpack_helper, compiler_dict, compiler_subscript: the literal forms by size and shape.
import builtins


def seq(n):
    return [f"{i}" for i in range(n)]


for n in (0, 1, 2, 3, 29, 30, 31, 32, 40):
    names = seq(n)
    x = 7
    for text in ("[" + ",".join(names) + "]", "(" + ",".join(names) + ("," if n else "") + ")", "{" + ",".join(names or ["0"]) + "}",
                 "[" + ",".join(["x"] * n) + "]", "(" + ",".join(["x"] * n) + ("," if n else "") + ")", "{" + ",".join(["x"] * (n or 1)) + "}",
                 "[*[1, 2]," + ",".join(names + ["x"]) + "]", "(*[1, 2]," + ",".join(names + ["x"]) + ",)", "{*[1, 2]," + ",".join(names + ["x"]) + "}",
                 "[" + ",".join(names + ["x"]) + ",*(1, 2)]", "{" + ",".join(names + ["x"]) + ",*{1, 2}}",
                 "{" + ",".join(f"{i}: {i}" for i in range(n)) + "}", "{" + ",".join(f"{i}: x" for i in range(n)) + "}",
                 "{" + ",".join(f"x + {i}: {i}" for i in range(n)) + "}",
                 "{**{1: 2}," + ",".join(f"{i}: {i}" for i in range(n)) + "}",
                 "{" + "".join(f"{i}: {i}," for i in range(n)) + "**{-1: 2}}",
                 "{" + "".join(f"{i}: {i}," for i in range(n)) + "**{-1: 2}, 'z': 3, **{'y': 4}}"):
        value = eval(text)
        print(n, type(value).__name__, len(value), sorted(value)[:2] if not isinstance(value, dict) else sorted(value.items(), key=str)[:2])

items = [1, 2, 3, 4, 5, 6]
print(items[1:3], items[:2], items[4:], items[:], items[::2], items[1:5:2], items[::-1])
items[1:3] = [9]
print(items)
items[1:2] += [7, 8]
print(items)
items[::2] = [0] * len(items[::2])
print(items)
del items[1:2]
print(items)


class Recorder:
    def __getitem__(self, key):
        print("get", key)
        return [1]

    def __setitem__(self, key, value):
        print("set", key, value)

    def __delitem__(self, key):
        print("del", key)


r = Recorder()
r[1:2]
r[1:2:3]
r[:]
r[1:2] = 3
r[1:2] += [4]
r[1:2:3] += [4]
r["k"] += [5]
del r[1:2]
r[1:2, 3]


def order(name, value=None):
    print(name)
    return value if value is not None else []


order("a")[order("b", 0):order("c", 0)]
order("d")[order("e", 0):order("f", 0)] = order("g")
order("h", [1])[order("i", 0):order("j", 1)] += order("k")
{order("l", 1): order("m", 2), order("n", 3): order("o", 4)}
{1: order("p", 2), 2: order("q", 3)}
{**order("r", {}), order("s", 1): order("t", 2)}

try:
    {1: 2, **3}
except TypeError as e:
    print(e)
try:
    {*1}
except TypeError as e:
    print(e)
try:
    [*1]
except TypeError as e:
    print(e)
try:
    (*1,)
except TypeError as e:
    print(e)
try:
    {[]: 1, 2: 3}
except TypeError as e:
    print(e)
try:
    {1, []}
except TypeError as e:
    print(e)
print({1: "a", 1: "b", 1.0: "c", True: "d"}, {1, 1.0, True}, [(), ((),), ((1, 2), 3)], {(1, 2): 3})


# LOAD_BUILD_CLASS reads the builtins, a global does not replace it.
def __build_class__(*args):
    raise AssertionError("global was used")


class Plain:
    pass


print(Plain.__name__)
del __build_class__
saved = builtins.__build_class__
try:
    calls = []

    def recording(func, name, *bases, **kwargs):
        calls.append(name)
        return saved(func, name, *bases, **kwargs)

    builtins.__build_class__ = recording

    class Seen(Plain):
        pass

    print(calls, Seen.__mro__[1].__name__)
    del builtins.__build_class__
    try:
        class Missing:
            pass
    except NameError as e:
        print(type(e).__name__, e)
finally:
    builtins.__build_class__ = saved


def imported():
    import os.path as p
    from os import sep as s, path
    return p is path, len(s)


print(imported())
