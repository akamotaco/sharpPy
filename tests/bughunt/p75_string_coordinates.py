#!/usr/bin/env python3
"""
p75: str coordinates are code points, as they are in CPython.

CPython needs no coordinate conversion: PyUnicode stores 1, 2 or 4 byte characters
(Include/cpython/unicodeobject.h PyUnicode_KIND / PyUnicode_READ), so len, s[i], slices, the value
find() returns, a pad width and a format precision are all character indices by construction
(Objects/unicodeobject.c unicode_getitem, unicode_slice, any_find_slice, Objects/stringlib/format.h
and the IS_* macros over Objects/unicodetype_db.h).

SharpPy stores a .NET string, i.e. UTF-16 code units, so an astral character is two units. Before
this probe the two systems were mixed at the call sites: len() counted units, s[1] could return half
a character, reversed() ran off the end, find() reported a unit offset, ljust/center/zfill and the
format mini-language padded to a unit width, ord() of an astral character returned a surrogate,
ordering sorted astral characters below U+E000, ascii() escaped a pair as two \\uXXXX halves, and
isalpha()/isdecimal()/isprintable()/isidentifier() asked .NET about half a character.

The empty-pattern and window rules below are pinned because CPython's ADJUST_INDICES
(Objects/unicodeobject.c:9091) clamps end but deliberately does not clamp start down to end, so
'abc'.find('', 4) is -1 while 'abc'.find('', 3) is 3, and because a UTF-16 search can match inside a
pair where CPython finds nothing at all.

Deliberately NOT pinned here, recorded in docs/known_gaps.md: CPython can hold U+D800 and U+DC00 as
two independent code points, which this representation reads as U+10000; CPython's isdigit() also
accepts characters carrying the digit property outside Nd (superscripts, circled digits);
isprintable() additionally rejects Cn (unassigned); isidentifier() requires NFC normalisation; and a
'\\U0000d800' escape in source is rejected by this parser, so lone surrogates can only be built with
chr() here.
"""


def show(label, fn):
    try:
        value = fn()
    except BaseException as exc:
        value = f"{type(exc).__name__}: {exc}"
    print(f"{label:<50} -> {ascii(value)}")


S = "a\U00010000b\U0001F600c"
AB = "abc"

# length and indexing
show("len_astral", lambda: len(S))
show("len_lone_high", lambda: len(chr(0xD800)))
show("len_empty", lambda: len(""))
show("index_1", lambda: S[1])
show("index_3", lambda: S[3])
show("index_neg_1", lambda: S[-1])
show("index_oob", lambda: S[5])
show("ord_astral", lambda: ord(S[1]))
show("ord_last", lambda: ord(S[-2]))
show("iterate", lambda: list(S))
show("enumerate", lambda: list(enumerate(S)))
show("reversed", lambda: "".join(reversed(S)))
show("reversed_list", lambda: list(reversed(S)))
show("reversed_empty", lambda: list(reversed("")))

# slices in both directions
show("slice_1_4", lambda: S[1:4])
show("slice_0_2", lambda: S[0:2])
show("slice_step2", lambda: S[::2])
show("slice_neg_step", lambda: S[::-1])
show("slice_oob", lambda: S[2:99])
show("slice_neg_2", lambda: S[-2:])
show("slice_empty_range", lambda: S[3:1])
show("slice_step_neg_2", lambda: S[::-2])
show("repeat_slice", lambda: S[1:2] * 2 == "\U00010000\U00010000")
show("slice_roundtrip_encode", lambda: S[1:2].encode("utf-8", "surrogatepass"))

# find family: window rules and pair alignment
show("find_astral", lambda: S.find("\U0001F600"))
show("find_astral_first", lambda: S.find("\U00010000"))
show("find_astral_window", lambda: S.find("\U0001F600", 0, 3))
show("find_astral_window_hit", lambda: S.find("\U0001F600", 0, 4))
show("find_neg_start", lambda: S.find("c", -3))
show("find_lone_low", lambda: S.find(chr(0xDC00)))
show("find_lone_high", lambda: S.find(chr(0xD800)))
show("find_empty_default", lambda: AB.find(""))
show("find_empty_1", lambda: AB.find("", 1))
show("find_empty_3", lambda: AB.find("", 3))
show("find_empty_4", lambda: AB.find("", 4))
show("find_empty_2_1", lambda: AB.find("", 2, 1))
show("find_empty_neg", lambda: AB.find("", -1))
show("find_empty_self", lambda: "".find(""))
show("rfind_empty_default", lambda: AB.rfind(""))
show("rfind_empty_0_2", lambda: AB.rfind("", 0, 2))
show("rfind_empty_1_1", lambda: AB.rfind("", 1, 1))
show("rfind_empty_2_0", lambda: AB.rfind("", 2, 0))
show("rfind_empty_9", lambda: AB.rfind("", 9))
show("rfind_astral", lambda: S.rfind("\U00010000"))
show("index_astral", lambda: S.index("\U0001F600"))
show("index_miss", lambda: S.index("z"))
show("index_empty_2_1", lambda: AB.index("", 2, 1))
show("rindex_astral", lambda: S.rindex("\U0001F600"))
show("rindex_empty_2_0", lambda: AB.rindex("", 2, 0))
show("count_astral", lambda: S.count("\U00010000"))
show("count_window", lambda: S.count("b", 1, 3))
show("count_window_neg", lambda: S.count("b", -4, -1))
show("count_lone_low", lambda: S.count(chr(0xDC00)))
show("count_empty_default", lambda: AB.count(""))
show("count_empty_1_2", lambda: AB.count("", 1, 2))
show("count_empty_2_1", lambda: AB.count("", 2, 1))
show("count_empty_9", lambda: AB.count("", 9))
show("count_empty_self", lambda: "".count(""))
show("startswith_astral", lambda: S.startswith("\U00010000", 1))
show("endswith_astral", lambda: S.endswith("\U0001F600", 2, 4))
show("endswith_astral_short", lambda: S.endswith("\U0001F600", 2, 3))
show("startswith_empty_rev", lambda: AB.startswith("", 2, 1))
show("endswith_empty_rev", lambda: AB.endswith("", 2, 1))
show("partition_astral", lambda: S.partition("b"))
show("split_astral", lambda: S.split("b"))

# width operations count characters, and the fill may be astral
show("ljust_astral", lambda: S.ljust(7))
show("ljust_astral_fill", lambda: S.ljust(7, "\U0001F600"))
show("rjust_astral", lambda: S.rjust(7, "-"))
show("center_astral", lambda: S.center(9, "*"))
show("center_odd", lambda: S.center(8, "*"))
show("zfill_astral", lambda: S.zfill(7))
show("zfill_sign", lambda: "-x".zfill(5))
show("ljust_no_grow", lambda: "abcdef".ljust(2))
show("ljust_two_char_fill", lambda: "a".ljust(5, "xy"))
show("center_empty_fill", lambda: "a".center(5, ""))
show("fill_not_str", lambda: "a".ljust(5, 1))

# the format mini-language
show("format_pad_right", lambda: format(S, ">8"))
show("format_pad_center", lambda: format(S, "^8"))
show("format_pad_left", lambda: format(S, "<8"))
show("format_precision", lambda: format("abcdef", ".3"))
show("format_precision_astral", lambda: format("a\U00010000bc", ".2"))
show("format_astral_fill", lambda: format("a", "\U0001F600>5"))
show("fstring_pad", lambda: f"{S:<8}|")

# ordering is by code point, not by code unit
show("order_astral", lambda: sorted(["\U00010000", "\U0000E000", "\U0000FFFF", "A"]))
show("compare_pair_gt_bmp", lambda: "\U00010000" > "\U0000FFFF")
show("compare_lone_high", lambda: chr(0xD800) < "\U0000E000")
show("compare_prefix_shorter", lambda: "\U00010000" < "\U00010000a")
show("min_mixed", lambda: min(["\U00010000", "z", "\U0000FFFF"]))
show("sort_pairs", lambda: sorted(["\U0001F600", "\U00010000", "\U00010001"]))

# character classes read characters
show("isalpha_astral", lambda: S.isalpha())
show("isalpha_sup_lo", lambda: "\U00010000".isalpha())
show("isalnum_astral", lambda: ("a\U00010000").isalnum())
show("isdigit_arabic", lambda: "\u0663".isdigit())
show("isdecimal_arabic", lambda: "\u0663".isdecimal())
show("isdecimal_sup", lambda: "\U0001D7D9".isdecimal())
show("isdigit_sup", lambda: "\U0001D7D9".isdigit())
show("isnumeric_roman", lambda: "\u2160".isnumeric())
show("isdecimal_ascii", lambda: "123".isdecimal())
show("isdecimal_mixed", lambda: "1a".isdecimal())
show("isspace_ideographic", lambda: "\u3000".isspace())
show("isspace_astral_mix", lambda: ("a\U00010000").isspace())
show("isprintable_astral", lambda: S.isprintable())
show("isprintable_newline", lambda: "a\n".isprintable())
show("isprintable_tab", lambda: "\t".isprintable())
show("isprintable_space", lambda: " ".isprintable())
show("isidentifier_astral", lambda: "\U00010000x".isidentifier())
show("isidentifier_digit_first", lambda: "1a".isidentifier())
show("isidentifier_underscore", lambda: "_a\U00010000".isidentifier())

# the same padding rule covers bytes; CPython transmogrify puts the extra byte on the left only
# when both the margin and the width are odd
show("bytes_center_3", lambda: b"ab".center(3, b"-"))
show("bytes_center_5", lambda: b"ab".center(5, b"-"))
show("bytes_center_7", lambda: b"ab".center(7, b"-"))
show("bytes_center_even", lambda: b"ab".center(4, b"-"))
show("bytes_center_odd_content", lambda: b"a".center(6, b"-"))
show("bytearray_center_5", lambda: bytearray(b"ab").center(5, b"-"))

# ascii() escapes a whole character
show("ascii_astral", lambda: ascii(S))
show("ascii_latin", lambda: ascii("\u00e9"))
show("ascii_mixed", lambda: ascii("a\U00010000"))
show("ascii_list", lambda: ascii([S, "\u00e9"]))

# the rest of str must still agree with the characters it holds
show("upper_len", lambda: len(S.upper()))
show("contains_pair", lambda: "\U0001F600" in S)
show("replace_astral", lambda: S.replace("\U00010000", "X"))
show("strip_astral", lambda: S.strip("a"))
show("encode_slice", lambda: S[1:2].encode("utf-8"))
show("join_astral", lambda: " ".join([S, "z"]))
show("center_after_join", lambda: " ".join([S, "z"]).center(12, "."))
