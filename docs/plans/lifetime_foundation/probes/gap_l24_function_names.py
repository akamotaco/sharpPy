def f():
    "doc"
    pass


for stmt in [
    'f.__name__ = "g"',
    'print("after name assign:", f.__name__, repr(f)[:18])',
    'del f.__doc__',
    'print("doc after del:", f.__doc__)',
    'del f.__name__',
    'f.__module__ = "m"',
    'print("module:", f.__module__)',
    'f.__dict__["z"] = 1',
    'print(f.z)',
]:
    try:
        exec(stmt)
    except Exception as e:
        print(type(e).__name__ + ":", e)
