# p70: one implementation per builtin, one rule for catching exceptions.
#
# The builtin half pins the operations whose PyBuiltinFunction table entry was a dead second copy
# behind a registration delegate. The exception half pins what CPython says: matching is
# PyObject_IsInstance against the raised object's own MRO, and a handler that is not an exception
# class is a TypeError even when the exception would otherwise escape.
#
# `except*` and ExceptionGroup.split() are not covered here: ExceptionGroup stores its items as CLR
# PyException objects, so a user-defined exception cannot even enter a group, and the unhandled
# re-raise casts PyNone. That is docs/known_gaps.md P0-33 with reproducers; pinning it here would
# freeze the wrong behaviour.


def s(label, fn):
    try:
        print(label, ascii(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", ascii(str(exc))[:88])


class Indexable:
    def __index__(self):
        return 41


class LenObj:
    def __len__(self):
        return 7


class BadLen:
    def __len__(self):
        return "no"


class Fmt:
    def __format__(self, spec):
        return "fmt[" + spec + "]"


class WithDict:
    pass


class MyErr(Exception):
    pass


class MyOther(Exception):
    pass


class SubValue(ValueError):
    pass


class Meta(type):
    pass


seq = __import__("collections.abc", fromlist=["x"])

# --- builtins that used to have two implementations ---
s("len_str", lambda: len("abcd"))
s("len_range", lambda: len(range(3, 20, 4)))
s("len_user", lambda: len(LenObj()))
s("len_bad", lambda: len(BadLen()))
s("len_none", lambda: len(None))
s("len_two", lambda: len("a", "b"))
s("len_kw", lambda: len("a", x=1))
s("abs_neg", lambda: abs(-5))
s("abs_float", lambda: abs(-2.5))
s("abs_subclass", lambda: abs(type("FI", (float,), {}) (-3.5)))
s("abs_complex", lambda: abs(3 + 4j))
s("abs_bool", lambda: abs(True))
s("abs_str", lambda: abs("x"))
s("isinstance_basic", lambda: (isinstance(1, int), isinstance(1.5, int), isinstance(True, int)))
s("isinstance_tuple", lambda: isinstance(1, (str, int)))
s("isinstance_abc", lambda: isinstance([], seq.Sequence))
s("isinstance_meta", lambda: isinstance(Meta("X", (), {}), Meta))
s("isinstance_bad", lambda: isinstance(1, 5))
s("issubclass_basic", lambda: (issubclass(SubValue, ValueError), issubclass(ValueError, SubValue)))
s("issubclass_tuple", lambda: issubclass(SubValue, (str, ValueError)))
s("issubclass_bad", lambda: issubclass(5, int))
s("bin_hex_oct", lambda: (bin(5), hex(-255), oct(8)))
s("bin_index", lambda: (bin(Indexable()), hex(Indexable()), oct(Indexable())))
s("bin_bool", lambda: (bin(True), hex(False), oct(True)))
s("bin_float", lambda: bin(1.0))
s("bin_big", lambda: hex(2 ** 70 + 1))
s("ascii_plain", lambda: ascii("café"))
s("ascii_repr", lambda: ascii([1, "é"]))
s("ascii_int", lambda: ascii(5))
s("vars_instance", lambda: (lambda o: (setattr(o, "a", 1), sorted(vars(o)))[1])(WithDict()))
# vars(cls) membership is deliberately not pinned here. Measured today: CPython's plain-class dict is
# ['__dict__', '__doc__', '__module__', '__weakref__'], SharpPy's lacks __dict__/__weakref__ and adds
# __qualname__; iterating vars(cls) additionally needs mappingproxy.__iter__. Registered in
# docs/known_gaps.md P0-28's surface list.

s("format_plain", lambda: format(3.14159, ".2f"))
s("format_default", lambda: format(42))
s("format_custom", lambda: format(Fmt(), "x"))
s("format_bad_spec", lambda: format(1, 5))
s("format_zero", lambda: format())
s("format_three", lambda: format(1, "", 3))
s("memoryview_bytes", lambda: bytes(memoryview(b"ab")))
s("memoryview_len", lambda: len(memoryview(b"abc")))
s("memoryview_str", lambda: memoryview("ab"))

# --- catching: the raised object's own MRO, nothing else ---
def _catch(handler, body):
    try:
        body()
    except handler as exc:
        return ("caught", type(exc).__name__, str(exc))
    return ("no-raise",)


def boom(exc):
    raise exc


s("except_exact", lambda: _catch(ValueError, lambda: boom(ValueError("v"))))
s("except_broader", lambda: _catch(Exception, lambda: boom(MyErr("m"))))
s("except_own_class", lambda: _catch(MyErr, lambda: boom(MyErr("m"))))
s("except_parent", lambda: _catch(ValueError, lambda: boom(SubValue("s"))))
s("except_child_handler", lambda: _catch(SubValue, lambda: boom(ValueError("v"))))
s("except_tuple", lambda: _catch((MyOther, MyErr), lambda: boom(MyErr("t"))))
s("except_tuple_no_match", lambda: _catch((MyOther,), lambda: boom(MyErr("t"))))
s("except_baseexception", lambda: _catch(BaseException, lambda: boom(MyErr("b"))))
s("except_nonclass", lambda: _catch(5, lambda: boom(MyErr("t"))))
s("except_nonclass_tuple", lambda: _catch((MyErr, 5), lambda: boom(MyOther("t"))))
s("except_str_handler", lambda: _catch("ValueError", lambda: boom(ValueError("v"))))
s("except_meta_class", lambda: _catch(Meta, lambda: boom(TypeError("t"))))


# Two unrelated classes that share a name must not catch each other.
def make_sibling():
    class MyErr(Exception):
        pass

    return MyErr


Sibling = make_sibling()
s("name_collision_not_caught", lambda: _catch(Sibling, lambda: boom(MyErr("same-name"))))
s("original_still_caught", lambda: _catch(MyErr, lambda: boom(MyErr("same-name"))))
