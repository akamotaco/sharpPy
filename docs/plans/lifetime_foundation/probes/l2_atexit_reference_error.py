# L2 acceptance. Becomes tests/bughunt/p95_atexit_reference_error.py when it passes.
# stderr is not compared: check "Exception ignored in atexit callback" by hand.
import atexit

print("ReferenceError:", [t.__name__ for t in ReferenceError.__mro__])
try:
    raise ReferenceError("gone")
except Exception as e:
    print(type(e).__name__, e, repr(e))

print("names:", sorted(n for n in dir(atexit) if not n.startswith("__")))
print("ncallbacks at start:", atexit._ncallbacks())


def show(*args, **kwargs):
    print("exit", args, sorted(kwargs.items()), flush=True)


print("register returns func:", atexit.register(show, "first") is show)


@atexit.register
def decorated():
    print("exit decorated", flush=True)


atexit.register(show, "kw", key="value")
print("ncallbacks:", atexit._ncallbacks())

# unregister compares with ==, removes every match, and is silent when nothing matches.
removed = lambda: print("must not run")
atexit.register(removed)
atexit.register(removed)
print("unregister:", atexit.unregister(removed), atexit._ncallbacks())
print("unregister unknown:", atexit.unregister(print), atexit._ncallbacks())

for bad in (None, 1):
    try:
        atexit.register(bad)
    except TypeError as e:
        print("TypeError:", e)
try:
    atexit.register()
except TypeError as e:
    print("TypeError:", e)


# _run_exitfuncs runs them now, newest first, and empties the list.
atexit._clear()
atexit.register(lambda: print("now 1", flush=True))
atexit.register(lambda: print("now 2", flush=True))
atexit._run_exitfuncs()
print("after run:", atexit._ncallbacks())


# Registered during exit: a callback that registers another.
def register_more():
    print("exit register_more", flush=True)
    atexit.register(print, "registered during exit", flush=True)


def raising():
    print("exit raising", flush=True)
    raise ValueError("from atexit")


class K:
    def __del__(self):
        print("__del__ after atexit", flush=True)


keep = K()
atexit.register(show, "oldest")
atexit.register(raising)
atexit.register(register_more)
print("end of module", flush=True)
