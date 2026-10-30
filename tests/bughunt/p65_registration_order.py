# p65: a builtin type's surface must exist before any object of that type is created.
#
# Every builtin type adds its TypeDict entries from a CLR static constructor, which runs the first
# time that CLR class is touched - not the first time the Python type is used. A script that named
# `bytes` without ever creating a bytes object therefore saw an almost empty type dictionary:
# dir(bytes) returned 24 names instead of 78, hasattr(bytes, "upper") was False, and
# getattr(bytes, "fromhex") raised AttributeError, while b"a".upper() worked normally. The observable
# surface of a type depended on which objects a program happened to build first.
#
# PyType now forces the class constructors in dependency order. The lines below deliberately read
# attributes off the types before any instance exists. The remaining known gaps in this area -
# frozenset, complex, memoryview, the dict views and the iterator types - are listed as P0-28 in
# docs/known_gaps.md, so this probe pins what works instead of pinning what is still missing.

def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", str(exc)[:70])


s("has_bytes_upper", lambda: hasattr(bytes, "upper"))
s("bytes_fromhex_type", lambda: type(bytes.fromhex).__name__)
s("has_ba_upper", lambda: hasattr(bytearray, "upper"))
s("ba_hex_type", lambda: type(bytearray.hex).__name__)
s("has_str_upper", lambda: hasattr(str, "upper"))
s("has_list_append", lambda: hasattr(list, "append"))
s("has_tuple_count", lambda: hasattr(tuple, "count"))
s("has_int_bit_length", lambda: hasattr(int, "bit_length"))
s("has_float_is_integer", lambda: hasattr(float, "is_integer"))
s("has_dict_get", lambda: hasattr(dict, "get"))
s("has_set_add", lambda: hasattr(set, "add"))
s("bytes_dir_has_upper", lambda: "upper" in dir(bytes))
s("ba_dir_has_decode", lambda: "decode" in dir(bytearray))
s("ba_dir_no_getnewargs", lambda: "__getnewargs__" in dir(bytearray))
s("getattr_default", lambda: getattr(bytes, "nope", "DEFAULT"))
s("getattr_type", lambda: type(getattr(bytes, "hex")).__name__)

# Only now create objects, so nothing above could have triggered registration as a side effect.
s("after_bytes_upper", lambda: b"ab".upper())
s("after_ba_hex", lambda: bytearray(b"\x01").hex())
s("still_has_bytes_upper", lambda: hasattr(bytes, "upper"))
