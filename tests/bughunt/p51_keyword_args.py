# P0-20: builtin keyword-argument acceptance follows CPython's METH_KEYWORDS table.
# CPython rejects keywords for METH_O/METH_VARARGS/METH_FASTCALL builtins with
#   "{Type}.{name}() takes no keyword arguments"
# and silently dropping them was a wrong answer, not a missing message.
# Measured against CPython 3.12.15.


def s(lbl, f):
    try:
        print(lbl, repr(f()))
    except Exception as e:
        print(lbl, "EXC", type(e).__name__, str(e)[:62])


# rejected: CPython has no METH_KEYWORDS on these
s("int_bit_length_kw", lambda: (1).bit_length(x=1))
s("float_is_integer_kw", lambda: (1.5).is_integer(x=1))
s("str_upper_kw", lambda: "a".upper(x=1))
s("str_count_kw", lambda: "aaa".count(sub="a"))
s("str_join_kw", lambda: ",".join(iterable=["a", "b"]))
s("str_replace_kw", lambda: "ab".replace(old="a", new="z"))
s("str_find_kw", lambda: "abc".find(sub="b"))
s("str_partition_kw", lambda: "a=b".partition(sep="="))
s("str_center_kw", lambda: "a".center(width=3))
s("str_strip_kw", lambda: " a ".strip(chars=" "))
s("float_hex_kw", lambda: (1.5).hex(x=1))
s("dict_copy_kw", lambda: {}.copy(x=1))
s("set_update_kw", lambda: set().update(a=1))

# accepted: METH_KEYWORDS, and the delegate must actually consume the value
s("str_split_sep", lambda: "a b".split(sep=" "))
s("str_split_maxsplit", lambda: "a b c".split(maxsplit=1))
s("str_rsplit_sep", lambda: "a b".rsplit(sep=" "))
s("str_expandtabs", lambda: "a\tb".expandtabs(tabsize=3))
s("str_encode", lambda: "é".encode(encoding="latin-1", errors="strict"))
s("str_splitlines", lambda: "a\nb".splitlines(keepends=True))
s("bytes_splitlines", lambda: b"a\nb".splitlines(keepends=True))
s("bytes_decode", lambda: b"ab".decode(encoding="utf-8"))
s("str_format", lambda: "{a}|{b}".format(a=1, b=2))
s("str_format_map", lambda: "{a}".format_map({"a": 7}))
s("dict_update", lambda: {}.update(a=1))
s("int_to_bytes_len", lambda: (5).to_bytes(length=2, byteorder="little"))
s("int_to_bytes_default", lambda: (5).to_bytes())
s("int_to_bytes_signed", lambda: (-1).to_bytes(1, signed=True))
s("bytes_count_empty_rev", lambda: "abc".count("", 3, 1))

# bound str methods still resolve through the type and keep the CPython bound form
print("bound_type", type("a".upper).__name__)
print("title", "it's a test-case 2nd".title())
