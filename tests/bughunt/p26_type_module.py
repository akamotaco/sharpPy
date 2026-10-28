# p26: type.__module__ resolution (metaclass self-lookup).
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


show("type.__module__", lambda: type.__module__)
show("type(type).__module__", lambda: type(type).__module__)
show("type(type) is type", lambda: type(type) is type)
show("type.__name__", lambda: type.__name__)
show("type(type).__name__", lambda: type(type).__name__)
show("type.__qualname__", lambda: type.__qualname__)
show("int.__module__", lambda: int.__module__)
show("str.__module__", lambda: str.__module__)
show("list.__module__", lambda: list.__module__)
show("object.__module__", lambda: object.__module__)
show("Exception.__module__", lambda: Exception.__module__)
show("ValueError.__module__", lambda: ValueError.__module__)


class C:
    pass


show("user C.__module__", lambda: C.__module__)
show("type(C).__module__", lambda: type(C).__module__)
show("C.__class__.__module__", lambda: C.__class__.__module__)
show("type(int).__module__", lambda: type(int).__module__)

import io
show("io TextIOWrapper.__module__", lambda: io.TextIOWrapper.__module__)
show("type(dict_keys).__module__", lambda: type({}.keys()).__name__)
show("type(iter([])).__module__", lambda: type(iter([])).__module__)
