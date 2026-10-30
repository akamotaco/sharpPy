# p67: the attribute builtins - argument texts, and which exceptions a default may swallow (P0-29).
#
# CPython's three-argument getattr suppresses AttributeError and nothing else, and hasattr is
# _PyObject_LookupAttr with the same rule. SharpPy had a second implementation of each in
# modules/BuiltinsModule.cs that won at registration time and caught every exception, so a ValueError
# raised by a user __getattr__ turned into the default value. It also had a branch that replaced a
# legitimate None attribute value with the default, which made "x = None" look like a missing
# attribute.
#
# The argument texts are measured, not invented: CPython uses "expected N arguments, got M" here
# (getattr splits it into "at least"/"at most"), and one message for the name argument across all
# four builtins - "attribute name must be string, not 'int'" - rather than a per-function variant.
#
# The deletion messages come from two different places in CPython, which is why they differ:
#   delattr(1, "real")      -> getset with no setter, Objects/descrobject.c:255-264
#   delattr(1, "__add__")   -> descriptor without tp_descr_set, Objects/object.c:1592-1598
#   delattr("s", "real")    -> nothing resolved, the dict path
#   delattr(obj, "__class__") -> object_set_class refusing deletion, typeobject.c:5712-5719
# An instance that HAS a __dict__ falls into that dict, so even a resolved class attribute answers
# "has no attribute" there - that asymmetry is pinned below too.

def s(l, f):
    try:
        print(l, ascii(f()))
    except Exception as exc:
        print(l, "EXC", type(exc).__name__ + ":", ascii(str(exc))[:66])


class C:
    cls_attr = 1

    def __init__(self):
        self.inst = 1

    @property
    def ro(self):
        return 1


class Boom:
    def __getattr__(self, name):
        raise ValueError("boom")


c = C()

print("--- getattr/hasattr/setattr/delattr arguments ---")
s("getattr_one", lambda: getattr(1))
s("getattr_four", lambda: getattr(1, "a", 2, 3))
s("getattr_name_int", lambda: getattr(1, 2))
s("getattr_name_int_default", lambda: getattr(1, 2, "D"))
s("hasattr_one", lambda: hasattr(1))
s("hasattr_three", lambda: hasattr(1, "a", 2))
s("hasattr_name_int", lambda: hasattr(1, 2))
s("setattr_two", lambda: setattr(1, "a"))
s("setattr_name_int", lambda: setattr(1, 2, 3))
s("delattr_one", lambda: delattr(1))
s("getattr_missing_no_default", lambda: getattr(1, "zz"))
s("getattr_missing_default", lambda: getattr(1, "zz", "D"))
s("getattr_dict_attr", lambda: getattr({"a": 1}, "get") is dict.get)

print("--- only AttributeError may be swallowed ---")
s("boom_getattr_default", lambda: getattr(Boom(), "anything", "D"))
s("boom_hasattr", lambda: hasattr(Boom(), "anything"))
s("boom_getattr_no_default", lambda: getattr(Boom(), "anything"))
s("none_value_is_not_missing", lambda: getattr(c, "__module__") is not None)


class HoldsNone:
    x = None


s("attr_is_none", lambda: getattr(HoldsNone(), "x", "DEFAULT"))
s("attr_is_none_no_default", lambda: getattr(HoldsNone(), "x"))
s("attr_is_none_hasattr", lambda: hasattr(HoldsNone(), "x"))

print("--- deletion ---")
s("del_instance_attr", lambda: (delattr(c, "inst"), hasattr(c, "inst")))
s("del_missing", lambda: delattr(c, "zz"))
s("del_class_attr_from_instance", lambda: delattr(c, "cls_attr"))
s("del_readonly_property", lambda: delattr(c, "ro"))
s("del_int_real", lambda: delattr(1, "real"))
s("del_str_real", lambda: delattr("s", "real"))
s("del_int_dunder_add", lambda: delattr(1, "__add__"))
s("del_list_dunder_len", lambda: delattr([], "__len__"))
s("del_inst_dunder_class", lambda: delattr(c, "__class__"))
s("del_user_class_dunder_class", lambda: delattr(C, "__class__"))
s("del_native_type_dunder_class", lambda: delattr(int, "__class__"))
s("del_instance_dunder_init", lambda: delattr(c, "__init__"))
s("del_type_attr_missing", lambda: delattr(C, "zz"))
print("p67 done")
