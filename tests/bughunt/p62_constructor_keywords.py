# P0-25: builtin constructor keyword signatures.
# Measured against CPython 3.12.15. PyType.CreateInstance used to hand the arguments to
# PyBuiltinFunction with kwargs replaced by null, so dict(a=1) built an empty dict and str(nope=1)
# built an empty string: silently wrong values, not just missing error messages.
# CPython has two different refusals - "X() takes no keyword arguments" for a positional-only
# signature, and "'x' is an invalid keyword argument for X()" for a named one.


def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as exc:
        print(label, "EXC", type(exc).__name__ + ":", str(exc)[:74])


class H(str):
    pass


class T(tuple):
    pass


# positional-only constructors
s("tuple_kw", lambda: tuple(iterable=[]))
s("list_kw", lambda: list(iterable=[]))
s("set_kw", lambda: set(iterable=[]))
s("frozenset_kw", lambda: frozenset(iterable=[]))
s("bool_kw", lambda: bool(x=1))
s("float_kw", lambda: float(x=1))
s("range_kw", lambda: range(stop=3))
s("slice_kw", lambda: slice(start=1, stop=3, step=2))

# named-parameter constructors
s("complex_named", lambda: complex(real=1, imag=2))
s("complex_bad_kw", lambda: complex(x=1))
s("str_object", lambda: str(object=5))
s("str_bad_kw", lambda: str(nope=1))
s("bytes_encode_kw", lambda: bytes("é", encoding="utf8"))
s("bytes_bad_kw", lambda: bytes(x=1))
s("bytearray_encode_kw", lambda: bytearray("é", encoding="utf8"))

# dict keywords are real items
s("dict_kw_only", lambda: dict(a=1, b=2))
s("dict_pos_plus_kw", lambda: dict([("a", 1)], b=2))
s("dict_empty", lambda: dict())

# str(bytes, encoding) decoding, which the dropped keywords also broke
s("str_bytes_default", lambda: str(b"ab"))
s("str_bytes_utf8", lambda: str(b"ab", "utf8"))
s("str_bytes_kw", lambda: str(b"ab", encoding="utf-8"))
s("str_bytes_latin1", lambda: str(b"\xff", "latin-1"))
s("str_int", lambda: str(65))
s("str_none", lambda: str(None))
s("str_enc_type_error", lambda: str(97, encoding=5))
s("str_bad_encoding", lambda: str(b"a", "no-such-codec"))
s("str_too_many", lambda: str("a", "b", "c", "d"))
s("sub_str_bytes", lambda: H(b"ab", "utf8"))
s("sub_str_plain", lambda: H("ab"))
s("sub_str_decode_str", lambda: H("ab", encoding="utf8"))
s("sub_str_int", lambda: H(65))

# text construction rules shared by bytes and bytearray
s("bytes_text_no_enc", lambda: bytes("é"))
s("bytearray_text_no_enc", lambda: bytearray("é"))
s("bytes_int", lambda: bytes(3))
s("bytearray_int", lambda: bytearray(3))
s("bytes_iter", lambda: bytes([1, 2]))
s("bytearray_iter", lambda: bytearray([1, 2]))
s("bytes_bad_encoding", lambda: bytes("a", "no-such-codec"))
s("bytearray_latin1", lambda: bytearray("é", "latin-1"))

# tuple.__new__ argument grammar
s("tuple_new_plain", lambda: tuple.__new__(tuple, [1, 2]))
s("tuple_new_kw", lambda: tuple.__new__(tuple, iterable=[]))
s("tuple_new_two_pos", lambda: tuple.__new__(tuple, [1], 1))
s("tuple_new_sub", lambda: tuple.__new__(T, [1, 2]))
s("tuple_new_foreign", lambda: tuple.__new__(int, ()))
s("tuple_new_sub_foreign", lambda: tuple.__new__(H, [1, 2]))
s("tuple_new_nonetype", lambda: tuple.__new__(type(None), ()))

# arity wording, which had the wrong shape
s("tuple_two_pos", lambda: tuple([1], 2))
s("list_two_pos", lambda: list([1], 2))
s("set_two_pos", lambda: set([1], 2))
s("dict_two_pos", lambda: dict({}, 2))
s("bool_two_pos", lambda: bool(1, 2))
