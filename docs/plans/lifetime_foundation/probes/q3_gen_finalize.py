# Q3 acceptance. Objects/genobject.c:70 _PyGen_Finalize closes a suspended generator when it dies.
# Observations happen after gc.collect(), except the generator kept alive to the end of the module.
import gc

log = []


def show(title):
    gc.collect()
    print(title, log)
    log.clear()


def g(tag):
    try:
        yield 1
    finally:
        log.append("finally " + tag)


it = g("del")
next(it)
del it
show("dropped:")

for x in g("break"):
    break
show("for-break:")


def scope():
    it = g("local")
    next(it)


scope()
show("local:")

# Never started: no frame has entered the try, so nothing runs.
it = g("not started")
del it
show("not started:")

# Exhausted: finally already ran once, and must not run again.
it = g("exhausted")
list(it)
del it
show("exhausted:")

# Explicitly closed: only once.
it = g("closed")
next(it)
it.close()
del it
show("closed:")


class CM:
    def __enter__(self):
        log.append("enter")
        return self

    def __exit__(self, *a):
        log.append("exit " + str(a[0].__name__ if a[0] else None))
        return False


def w():
    with CM():
        yield 1


it = w()
next(it)
del it
show("with:")


def catches():
    try:
        yield 1
    except GeneratorExit:
        log.append("got GeneratorExit")
        raise


it = catches()
next(it)
del it
show("GeneratorExit:")


# A generator with no handler has nothing to run (Objects/genobject.c:399 exception_handler_depth).
def plain():
    yield 1
    log.append("must not run")


it = plain()
next(it)
del it
show("no try:")


def outer():
    yield from inner()


def inner():
    try:
        yield 1
    finally:
        log.append("inner finally")


it = outer()
next(it)
del it
show("yield from:")


# Ignoring GeneratorExit: reported as unraisable on stderr, not raised into the program.
def stubborn():
    try:
        yield 1
    except GeneratorExit:
        yield 2


it = stubborn()
next(it)
del it
show("ignored GeneratorExit:")


def raising():
    try:
        yield 1
    finally:
        raise ValueError("from finally")


it = raising()
next(it)
del it
show("raising finally:")


def at_exit():
    try:
        yield 1
    finally:
        print("finally at exit")


keep = at_exit()
next(keep)
print("end of module")
