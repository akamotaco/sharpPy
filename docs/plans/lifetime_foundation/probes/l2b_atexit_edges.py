# L2 follow-up acceptance, written by the reviewer. The bound-method case of unregister stays out: it
# depends on P0-54 and is checked there.
import atexit


def t(label, f):
    try:
        print(label, repr(f()))
    except TypeError as e:
        print(label, "TypeError:", e)


t("unregister no args", lambda: atexit.unregister())
t("unregister two args", lambda: atexit.unregister(print, print))
t("unregister keyword", lambda: atexit.unregister(func=print))
t("_ncallbacks arg", lambda: atexit._ncallbacks(1))
t("_clear arg", lambda: atexit._clear(1))
t("_run_exitfuncs arg", lambda: atexit._run_exitfuncs(1))


class Hook:
    def __init__(self, name):
        self.name = name

    def __call__(self):
        print("ran", self.name)

    __hash__ = None


class Truthy(Hook):
    def __eq__(self, other):
        return 1


class Falsy(Hook):
    def __eq__(self, other):
        return []


class NotImpl(Hook):
    def __eq__(self, other):
        return NotImplemented


class Reflected:
    def __eq__(self, other):
        print("reflected eq")
        return True


class Raises(Hook):
    def __eq__(self, other):
        raise ValueError("eq raised")


# unregister uses PyObject_RichCompareBool: truth value, reflected operand, exceptions propagate.
for hook, other in [(Truthy("truthy"), print), (Falsy("falsy"), print),
                    (NotImpl("reflected"), Reflected())]:
    atexit._clear()
    atexit.register(hook)
    print(hook.name, atexit.unregister(other))
    atexit._run_exitfuncs()

atexit._clear()
atexit.register(Raises("raises"))
try:
    atexit.unregister(print)
except ValueError as e:
    print("ValueError:", e)
atexit._run_exitfuncs()

# The slot is not emptied before the call, so a nested run reaches the running callback again.
atexit._clear()
once = []


def again():
    print("again", len(once))
    if not once:
        once.append(1)
        atexit._run_exitfuncs()
        print("after inner", atexit._ncallbacks())


atexit.register(print, "older")
atexit.register(again)
atexit._run_exitfuncs()
print("end", atexit._ncallbacks())
