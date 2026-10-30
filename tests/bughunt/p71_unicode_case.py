# p71: str case operations follow CPython's FULL case mappings (P0-31).
#
# Everything here runs through core/PyUnicodeCase.cs, which is a transliteration of
# Objects/unicodeobject.c do_upper / do_lower / do_title / do_capitalize / do_swapcase /
# do_casefold / unicode_isupper_impl / unicode_islower_impl / unicode_istitle_impl, on top of
# core/UnicodeCaseData.cs (mappings measured from CPython 3.12.15, properties parsed from
# Objects/unicodetype_db.h).
#
# Output is UTF-8 hex, not the characters themselves: SharpPy still indexes str by UTF-16 code unit
# (docs/known_gaps.md P0-34), so len()/indexing/ascii() of a non-BMP result would report that bug
# instead of the case mapping. hex() compares the bytes both interpreters hold.


def hx(fn):
    try:
        r = fn()
    except Exception as exc:
        return "EXC %s: %s" % (type(exc).__name__, ascii(str(exc))[:70])
    if isinstance(r, str):
        return r.encode("utf-8").hex()
    return "T" if r else "F"


def s(label, fn):
    print(label, hx(fn))


S = "\u00df"                      # sharp s: full mapping expands to two characters
DOTI = "\u0130"                   # I with dot above: lower expands to i + combining dot
TIGO = "\u01c5"                   # DZ caron: the odd one out, a titlecase character
LIGFI = "\ufb01"                  # fi ligature
MICRON = "\u00b5"                 # micro sign, not Greek mu


s("upper_sharp_s", lambda: S.upper())
s("lower_sharp_s", lambda: S.lower())
s("upper_sharp_s_twice", lambda: (S + S).upper())
s("upper_strasse", lambda: "STRASSE".upper())
s("lower_dotted_i", lambda: DOTI.lower())
s("upper_dotted_i", lambda: DOTI.upper())
s("lower_dotless_i", lambda: "\u0131".lower())
s("upper_dotless_i", lambda: "\u0131".upper())
s("upper_ligature_fi", lambda: LIGFI.upper())
s("fold_ligature_fi", lambda: LIGFI.casefold())
s("upper_long_s", lambda: "\u017f".upper())
s("fold_long_s", lambda: "\u017f".casefold())
s("upper_micro_sign", lambda: MICRON.upper())
s("fold_micro_sign", lambda: MICRON.casefold())
s("upper_title_3", lambda: TIGO.upper())
s("lower_title_3", lambda: TIGO.lower())
s("title_title_3", lambda: TIGO.title())
s("capitalize_title_3", lambda: TIGO.capitalize())
s("swapcase_title_3", lambda: TIGO.swapcase())
s("istitle_title_3", lambda: TIGO.istitle())
s("isupper_title_3", lambda: TIGO.isupper())
s("islower_title_3", lambda: TIGO.islower())

# Other_Uppercase / Other_Lowercase: no case mapping of their own, but the properties are set, so
# .NET-style predicates answer False while CPython answers True.
s("isupper_roman_1", lambda: "\u2160".isupper())
s("islower_roman_1", lambda: "\u2170".islower())
s("isupper_circled_a", lambda: "\u24b6".isupper())
s("islower_circled_a", lambda: "\u24b6".islower())
s("istitle_circled_a", lambda: "\u24b6".istitle())
s("upper_roman_1", lambda: "\u2160".upper())
s("lower_roman_1", lambda: "\u2160".lower())
s("swapcase_roman_1", lambda: "\u2160".swapcase())

# Greek capital sigma: the only context-sensitive mapping. lower_ucs4 asks whether a cased character
# precedes (ignoring case-ignorable ones) and whether one follows.
s("lower_sigma_alone", lambda: "\u03a3".lower())
s("lower_sigma_pair", lambda: "\u03a3\u03a3".lower())
s("lower_sigma_word", lambda: "\u039f\u0394\u039f\u03a3".lower())
s("lower_sigma_then_space", lambda: "\u03a3 ".lower())
s("lower_sigma_apostrophe", lambda: "\u03a3'\u03a3".lower())
s("lower_sigma_combining", lambda: "\u03a3\u0301".lower())
s("lower_sigma_combining_before", lambda: "\u0301\u03a3".lower())
s("lower_sigma_digit_between", lambda: "\u03a31\u03a3".lower())
s("lower_final_sigma", lambda: "\u03c2".lower())
s("upper_final_sigma", lambda: "\u03c2".upper())
s("swapcase_sigma_word", lambda: "\u039f\u0394\u039f\u03a3".swapcase())

# do_title: the word rule is "was the previous character cased", not "was the previous a boundary".
s("title_apostrophe", lambda: "it's a test-case 2nd".title())
s("title_underscores", lambda: "foo_bar__baz".title())
s("title_digits", lambda: "a1b2c".title())
s("title_sharp_s", lambda: "\u00dfe".title())
s("title_after_roman", lambda: "\u2160\u00df".title())
s("title_dotted_i", lambda: (DOTI + "x").title())
s("title_sigma", lambda: "\u039f\u0394\u039f\u03a3".title())
s("capitalize_sigma", lambda: "\u039f\u0394\u039f\u03a3".capitalize())
s("capitalize_empty", lambda: "".capitalize())
s("capitalize_sharp_s", lambda: S.capitalize())
s("capitalize_roman", lambda: "\u2160\u2170".capitalize())

s("swapcase_mixed", lambda: "aB1cD \u00df \u0130".swapcase())
s("swapcase_sigma", lambda: "\u03a3".swapcase())
s("swapcase_empty", lambda: "".swapcase())
s("fold_sigma", lambda: "\u03a3".casefold())
s("fold_mixed", lambda: "STRA\u00dfe \ufb01 \u2160".casefold())
s("fold_empty", lambda: "".casefold())

# Predicates on multi-character strings, including the "at least one cased character" requirement.
s("isupper_empty", lambda: "".isupper())
s("isupper_digits", lambda: "123".isupper())
s("isupper_mixed", lambda: "AB1".isupper())
s("isupper_one_lower", lambda: "AB1c".isupper())
s("isupper_sharp_s", lambda: S.isupper())
s("isupper_upper_sharp_s", lambda: "SS".isupper())
s("islower_one_upper", lambda: "ab1C".islower())
s("islower_sharp_s", lambda: S.islower())
s("istitle_word", lambda: "Hello World".istitle())
s("istitle_double_upper", lambda: "Hello WORLD".istitle())
s("istitle_lower_first", lambda: "hello World".istitle())
s("istitle_roman_only", lambda: "\u2160".istitle())
s("istitle_apostrophe", lambda: "It'S".istitle())
s("istitle_empty", lambda: "".istitle())

# The behaviour that was already correct must stay correct, ASCII included.
s("upper_ascii", lambda: "aA zZ \t1".upper())
s("lower_ascii", lambda: "aA zZ \t1".lower())
s("upper_accent", lambda: "\u00e9\u00c9".upper())
s("lower_accent", lambda: "\u00e9\u00c9".lower())
s("upper_umlaut_s", lambda: "\u00df".upper().lower())
s("astral_upper", lambda: "\U00010400".upper())
s("astral_lower", lambda: "\U00010400".lower())
s("astral_fold", lambda: "\U00010400".casefold())

# Arity and receiver checks stay on the descriptor path.
s("upper_arg", lambda: "a".upper(1))
s("fold_arg", lambda: "a".casefold(1))
s("upper_on_int", lambda: str.upper(5))
s("fold_on_list", lambda: str.casefold([1]))

# A NUL character is an ordinary character inside a string (it is only illegal in the source file,
# which is a separate gap), and the ASCII fast path, which starts copying at the first character that
# moves, must not decide anything by looking for a sentinel value.
s("nul_ascii_upper", lambda: "a\x00b".upper())
s("nul_ascii_lower", lambda: "A\x00B".lower())
s("nul_then_sharp", lambda: "a\x00\u00df".upper())
s("nul_fold", lambda: "A\x00\u00df".casefold())
s("nul_isupper", lambda: "A\x00B".isupper())

# str subclasses keep going through the same single implementation.
class MyStr(str):
    pass


s("subclass_upper", lambda: MyStr(S).upper())
s("subclass_fold", lambda: MyStr(LIGFI).casefold())
s("subclass_title", lambda: MyStr("it's").title())
s("subclass_isupper", lambda: MyStr("\u2160").isupper())
