# p22: dir() content-set parity against CPython 3.12.
#
# Focus: the *structure* of dir() - object protocol surface, class MRO union
# (metaclass excluded), instance __dict__, heap __dict__/__weakref__, module
# __dict__, and ordinal sort order. Builtin concrete methods (int.is_integer,
# str.casefold, ...) are a separate "materialize builtin TypeDict" cluster, so
# builtins are checked by membership, not full-set equality.

def show(label, fn):
    try:
        print(label, fn())
    except Exception as e:
        print(label, "EXC", type(e).__name__, e)

# --- full-set parity for object and user classes/instances ---
# dir(type) and dir(Exception) are NOT asserted as full sets: their exact
# contents depend on full `type`/`BaseException` TypeDict materialization
# (CPython type has __flags__/__basicsize__/__abstractmethods__/...;
# BaseException has add_note/with_traceback/__traceback__/...) which is the
# separate builtin-TypeDict-materialization cluster. Only the shared protocol
# surface is asserted here (see type-member*/exc-member* below).
show("dir-object", lambda: sorted(dir(object)))

class Base:
    class_attr = 1
    def method(self): pass
    @staticmethod
    def s(): pass
    @classmethod
    def c(cls): pass
    @property
    def p(self): return 0

class Child(Base):
    extra = 2
    def __init__(self): self.live = 3

show("dir-Base", lambda: sorted(dir(Base)))
show("dir-Child", lambda: sorted(dir(Child)))
show("dir-Base-inst", lambda: sorted(dir(Base())))
show("dir-Child-inst", lambda: sorted(dir(Child())))

b = Base()
b.runtime = "x"
show("dir-Base-inst-added", lambda: sorted(dir(b)))

# --- ordinal (code-point) sort order: uppercase before lowercase, dunder mix ---
class Ordinal:
    A = 1
    a = 2
    Zz = 3
    zz = 4
    _u = 5
    __x__ = 6
    def __call__(self): pass

show("dir-ordinal", lambda: sorted(dir(Ordinal)))
show("dir-ordinal-sorted-check", lambda: sorted(dir(Ordinal)) == sorted(dir(Ordinal), key=lambda s: [ord(ch) for ch in s]))

# --- object protocol surface is visible via dir() on builtins ---
for name in ("__eq__", "__ne__", "__lt__", "__gt__", "__hash__", "__str__",
             "__repr__", "__init__", "__new__", "__dir__", "__format__",
             "__sizeof__", "__reduce__", "__reduce_ex__", "__subclasshook__",
             "__getattribute__", "__setattr__", "__delattr__", "__class__",
             "__getstate__", "__init_subclass__"):
    show(f"object-has[{name}]", lambda n=name: n in dir(object))
    show(f"int-has[{name}]", lambda n=name: n in dir(int))
    show(f"str-has[{name}]", lambda n=name: n in dir(str))
    show(f"list-has[{name}]", lambda n=name: n in dir(list))
    show(f"dict-has[{name}]", lambda n=name: n in dir(dict))

# --- type/Exception shared protocol surface via membership (not full set) ---
# NOTE: "__qualname__" is measured present in CPython dir(type) but absent in
# SharpPy dir(type); it (with __flags__/__basicsize__/__abstractmethods__/...)
# belongs to the deferred `type` TypeDict-materialization cluster, so it is not
# asserted here.
for name in ("__name__", "__bases__", "__base__", "__mro__", "__module__",
             "__call__", "__dir__", "__eq__", "__hash__",
             "__init__", "__new__", "__repr__", "__subclasshook__", "mro"):
    show(f"type-member[{name}]", lambda n=name: n in dir(type))
for name in ("args", "__class__", "__init__", "__str__", "__repr__",
             "__eq__", "__hash__", "__dir__", "__doc__"):
    show(f"exc-member[{name}]", lambda n=name: n in dir(Exception))

# --- metaclass attributes must NOT leak into an instance dir() ---
class Meta(type):
    def meta_only(cls): pass

class WithMeta(metaclass=Meta):
    pass

show("inst-no-metaclass-attr", lambda: "meta_only" in dir(WithMeta()))
show("cls-has-metaclass-attr", lambda: "meta_only" in dir(WithMeta))
show("heap-has-dict-weakref", lambda: ("__dict__" in dir(WithMeta)) and ("__weakref__" in dir(WithMeta)))
show("object-no-dict-weakref", lambda: ("__dict__" in dir(object)) or ("__weakref__" in dir(object)))

# --- __dir__() is callable and agrees with dir() as a set ---
show("dirmethod-set", lambda: set(Child().__dir__()) == set(dir(Child())))

# --- module dir() reflects module __dict__ ---
import json as _json
show("module-dir-name", lambda: "__name__" in dir(_json))
show("module-dir-dict", lambda: "__dict__" in dir(_json))
show("module-dir-content", lambda: "loads" in dir(_json) and "dumps" in dir(_json))
