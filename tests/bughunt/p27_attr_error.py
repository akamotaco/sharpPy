# p27: attribute-error message parity (class object vs instance vs module).
# Native-type "del <attr> of immutable type" is a separate deferred item.
def err(label, fn):
    try:
        fn()
        print(label, "ok")
    except Exception as e:
        print(label, type(e).__name__ + ":", e)


err("int-getattr", lambda: int.__len__)
err("str-getattr", lambda: str.__nope__)


class C:
    pass


err("user-class-getattr", lambda: C.foo)
err("instance-getattr", lambda: C().__len__)
err("int-instance-getattr", lambda: (5).__iter__)


def del_user():
    del C.foo


err("user-class-delattr", del_user)

import collections

err("module-getattr", lambda: collections.nonexistent)
err("nested-class-getattr", lambda: type("Outer", (), {})().nope)
