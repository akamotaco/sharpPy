"""Every property of every code point, by digest, and normalization of what is hard to normalize."""
import hashlib
import sys
import unicodedata as u


def digest(function):
    state = hashlib.sha256()
    for code in range(0, sys.maxunicode + 1):
        if 0xD800 <= code < 0xE000 and function is not u.category:
            continue
        state.update(repr(function(chr(code))).encode())
    return state.hexdigest()[:16]


print(u.unidata_version)
for function in (u.category, u.bidirectional, u.east_asian_width, u.combining, u.mirrored, u.decomposition,
                 lambda c: u.decimal(c, None), lambda c: u.digit(c, None), lambda c: u.numeric(c, None),
                 lambda c: u.name(c, None)):
    print(getattr(function, "__name__", "?"), digest(function))

for form in ("NFC", "NFD", "NFKC", "NFKD"):
    state = hashlib.sha256()
    for code in range(0, sys.maxunicode + 1, 1):
        if 0xD800 <= code < 0xE000:
            continue
        state.update(u.normalize(form, chr(code)).encode())
    print(form, state.hexdigest()[:16])
    # canonical ordering, blocking, Hangul and exclusions together
    for text in ("ạ̇", "ạ̇", "각", "각", "क़", "ﬁ́",
                 "ẛ̣", "̈́", "àb́", "q̣̇", "\U0001d15e", "Å", "한글",
                 "한", "à֮̀̕b"):
        result = u.normalize(form, text)
        print(ascii(result), u.is_normalized(form, text))

print(u.lookup("LATIN SMALL LETTER A"), u.lookup("HANGUL SYLLABLE HAN"), u.name("한"), u.name("一"))
for call in (lambda: u.name("\ud800"), lambda: u.lookup("nope"), lambda: u.decimal("a"), lambda: u.digit("a"),
             lambda: u.numeric("a"), lambda: u.normalize("X", "a"), lambda: u.normalize(1, "a"),
             lambda: u.normalize("NFC", 1), lambda: u.category("ab"), lambda: u.category(1), lambda: u.category("")):
    try:
        print(call())
    except Exception as error:
        print(type(error).__name__, error)
