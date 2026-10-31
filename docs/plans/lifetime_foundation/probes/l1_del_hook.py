# L1 acceptance. Becomes tests/bughunt/p93_del_hook.py when it passes.
# slot_tp_finalize reaches __del__ through lookup_maybe_method (Objects/typeobject.c:9071, :2188):
# a descriptor is bound the normal way, and a None in the subclass hides the parent.
import gc

log = []


def show(title):
    gc.collect()
    print(title, log)
    log.clear()


class P:
    def __del__(self):
        log.append("P")


class NoneDel(P):
    __del__ = None


o = NoneDel()
del o
show("None shadows parent:")


class CallableHook:
    def __call__(self, *args):
        log.append(("hook args", len(args)))


class WithCallable:
    __del__ = CallableHook()


o = WithCallable()
del o
show("callable object:")


class WithStatic:
    @staticmethod
    def __del__():
        log.append("static")


o = WithStatic()
del o
show("staticmethod:")


class WithClassMethod:
    @classmethod
    def __del__(cls):
        log.append(("classmethod", cls.__name__))


o = WithClassMethod()
del o
show("classmethod:")


class Getter:
    def __get__(self, instance, owner):
        log.append(("__get__", instance is not None))
        return lambda: log.append("bound by __get__")


class WithGetter:
    __del__ = Getter()


o = WithGetter()
del o
show("custom descriptor:")
