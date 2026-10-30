# p72: str.encode / bytes(str, enc) / _codecs.*_encode follow CPython's strict encoder (P0-34 pre-work).
#
# SharpPy used to own several codec tables: str.encode picked a .NET Encoding itself, bytes(str, enc)
# had a four-name table, str(bytes, enc, errors) a three-name one, bytes.decode another, and
# PyString.Encode had a faithful refusal that nothing called. .NET replaces what it cannot encode, so
# the live paths answered b'?' for 'é'.encode('ascii') and b'\xef\xbf\xbd' for a lone surrogate,
# while CPython refuses. utf-16/utf-32 were folded onto their -le aliases, so the BOM disappeared.
#
# Everything now goes through PyString.Encode / PyStringNewArguments.DecodeBytes.
#
# Astral text is deliberately kept out of anything the interpreter prints as a str: SharpPy still
# indexes str by UTF-16 code unit (docs/known_gaps.md P0-34), so repr(str) would report that gap
# instead of the encoder. Byte-level results are compared as hex, and they are exact.

import _codecs

A = chr(0x1F600)          # emoji: one character, four UTF-8 bytes, a surrogate pair in UTF-16
B = chr(0x10400)          # deseret capital A
EAC = chr(0xE9)           # e with acute: one byte in latin-1, two in utf-8, refused by ascii
EA2 = chr(0xEA)
LONE_HI = chr(0xD800)
LONE_LO = chr(0xDC00)
ESC_HI = chr(0xDC80)      # the only surrogate range surrogateescape can carry back to a byte


def hx(fn):
    try:
        r = fn()
    except Exception as exc:
        return "EXC %s: %s" % (type(exc).__name__, ascii(str(exc))[:80])
    if isinstance(r, bytes):
        return r.hex()
    if isinstance(r, tuple):
        return "(" + ", ".join(x.hex() if isinstance(x, bytes) else str(x) for x in r) + ")"
    return repr(r)


def s(label, fn):
    print(label, hx(fn))


# --- the codec table, by bytes -------------------------------------------------------------
for name in ("utf-8", "utf-16", "utf-16-le", "utf-16-be",
             "utf-32", "utf-32-le", "utf-32-be", "latin-1", "ascii"):
    s("bmp_%s" % name.replace("-", ""), lambda n=name: "ab".encode(n))
    s("eacute_%s" % name.replace("-", ""), lambda n=name: EAC.encode(n))
    s("astral_%s" % name.replace("-", ""), lambda n=name: A.encode(n))

# utf-16 and utf-32 carry a byte-order mark; the -le/-be codecs must not.
s("bom_utf16_empty", lambda: "".encode("utf-16"))
s("bom_utf32_empty", lambda: "".encode("utf-32"))
s("bom_utf16le_empty", lambda: "".encode("utf-16-le"))
s("bom_utf32be_empty", lambda: "".encode("utf-32-be"))
s("alias_u16", lambda: "ab".encode("U16"))
s("alias_utf16_under", lambda: "ab".encode("utf_16"))
s("alias_l1", lambda: EAC.encode("l1"))
s("alias_646", lambda: "ab".encode("646"))

# --- refusals: CPython's messages, with character positions -------------------------------
s("ascii_one", lambda: EAC.encode("ascii"))
s("ascii_leading", lambda: ("a" + EAC).encode("ascii"))
s("ascii_trailing", lambda: (EAC + "b").encode("ascii"))
s("ascii_run", lambda: ("a" + EAC + EA2 + "b").encode("ascii"))
s("ascii_two_runs", lambda: (EAC + EA2 + "b" + EA2).encode("ascii"))
s("latin1_astral", lambda: A.encode("latin-1"))
s("ascii_astral", lambda: B.encode("ascii"))
s("lone_hi_utf8", lambda: LONE_HI.encode("utf-8"))
s("lone_lo_utf8", lambda: LONE_LO.encode("utf-8"))
s("lone_hi_mid", lambda: ("a" + LONE_HI + "b").encode("utf-8"))
s("lone_hi_latin1_ok", lambda: LONE_HI.encode("latin-1"))
s("lone_hi_utf16", lambda: LONE_HI.encode("utf-16"))
s("empty_utf8", lambda: "".encode("utf-8"))
s("nul_utf8", lambda: "a\x00b".encode("utf-8"))
s("unknown_codec", lambda: "ab".encode("bogus-codec"))
s("unknown_codec_case", lambda: "ab".encode("BOGUSCodec"))

# --- error handlers ------------------------------------------------------------------------
s("h_ignore_one", lambda: EAC.encode("ascii", "ignore"))
s("h_ignore_mixed", lambda: ("a" + EAC + "b").encode("ascii", "ignore"))
s("h_ignore_astral", lambda: (A + "x").encode("ascii", "ignore"))
s("h_replace_one", lambda: EAC.encode("ascii", "replace"))
s("h_replace_astral", lambda: A.encode("ascii", "replace"))
s("h_replace_run", lambda: (EAC + EA2 + "b").encode("ascii", "replace"))
s("h_replace_lone", lambda: LONE_HI.encode("utf-8", "replace"))
s("h_ignore_lone", lambda: LONE_HI.encode("utf-8", "ignore"))
s("h_pass_lone", lambda: LONE_HI.encode("utf-8", "surrogatepass"))
s("h_pass_lone_utf16", lambda: LONE_HI.encode("utf-16-le", "surrogatepass"))
s("h_escape_escapable", lambda: ESC_HI.encode("utf-8", "surrogateescape"))
s("h_escape_two", lambda: (ESC_HI + chr(0xDCFF)).encode("utf-8", "surrogateescape"))
s("h_escape_not_escapable", lambda: LONE_HI.encode("utf-8", "surrogateescape"))
s("h_unknown_unused", lambda: "e".encode("ascii", "bogus"))
s("h_unknown_used", lambda: EAC.encode("ascii", "bogus"))
s("h_upper_unused", lambda: "e".encode("ascii", "BAD"))
s("h_empty_unused", lambda: "e".encode("ascii", ""))
s("h_strict_explicit", lambda: "ab".encode("ascii", "strict"))
s("h_kwargs", lambda: EAC.encode(encoding="ascii", errors="ignore"))

# --- escape handlers: consulted per character, and the escape is always ASCII bytes ---------
s("h_bs_ascii", lambda: EAC.encode("ascii", "backslashreplace"))
s("h_bs_run", lambda: ("a" + EAC + EA2 + "b").encode("ascii", "backslashreplace"))
s("h_bs_latin1_ok", lambda: EAC.encode("latin-1", "backslashreplace"))
s("h_bs_nbsp", lambda: chr(0xA0).encode("ascii", "backslashreplace"))
s("h_bs_lone", lambda: LONE_HI.encode("ascii", "backslashreplace"))
s("h_bs_lone_utf8", lambda: LONE_HI.encode("utf-8", "backslashreplace"))
s("h_xml_ascii", lambda: EAC.encode("ascii", "xmlcharrefreplace"))
s("h_xml_run", lambda: ("a" + EAC + EA2 + "b").encode("ascii", "xmlcharrefreplace"))
s("h_xml_latin1_ok", lambda: EAC.encode("latin-1", "xmlcharrefreplace"))
s("h_xml_lone", lambda: LONE_HI.encode("utf-8", "xmlcharrefreplace"))
s("h_astral_bs", lambda: B.encode("ascii", "backslashreplace"))
s("h_astral_xml", lambda: B.encode("latin-1", "xmlcharrefreplace"))
# namereplace는 Unicode 이름 데이터베이스가 필요해서 별도 항목으로 남긴다 (docs/known_gaps.md).

# --- argument handling ---------------------------------------------------------------------
s("too_many_args", lambda: "a".encode("ascii", "strict", "x"))
s("bad_encoding_type", lambda: "a".encode(1))
s("bad_errors_type", lambda: "a".encode("ascii", 2))


class MyStr(str):
    pass


s("subclass_bytes", lambda: MyStr("ab").encode("utf-16-le").hex())
s("subclass_error", lambda: MyStr(EAC).encode("ascii"))

# --- bytes(str, encoding) and str(bytes, encoding) share the same two implementations ------
s("bytes_ctor_bmp", lambda: bytes("ab", "utf-16-le"))
s("bytes_ctor_strict", lambda: bytes(EAC, "ascii"))
s("bytes_ctor_lone", lambda: bytes(LONE_HI, "utf-8"))
s("bytes_ctor_unknown", lambda: bytes("ab", "bogus-codec"))
s("bytes_ctor_no_arg", lambda: bytes("ab"))
s("str_ctor_utf16le", lambda: "ab".encode("utf-16-le").decode("utf-16-le").encode("utf-8").hex())
s("str_ctor_utf32be", lambda: "ab".encode("utf-32-be").decode("utf-32-be").encode("utf-8").hex())
s("str_ctor_bom_only", lambda: "".encode("utf-16").decode("utf-16"))
s("decode_bom_stripped", lambda: ("ab").encode("utf-32").decode("utf-32").hex())
s("decode_utf16_no_mark", lambda: "ab".encode("utf-16-le").decode("utf-16").hex())
s("decode_latin1", lambda: EAC.encode("latin-1").decode("latin-1").encode("utf-8").hex())
s("decode_bad_codec", lambda: "ab".decode("bogus-codec"))

# --- _codecs entry points: (bytes, characters consumed) -------------------------------------
s("c_utf8_bmp", lambda: _codecs.utf_8_encode("ab"))
s("c_utf8_eacute", lambda: _codecs.utf_8_encode(EAC))
s("c_utf16_bmp", lambda: _codecs.utf_16_encode("ab"))
s("c_utf16le_bmp", lambda: _codecs.utf_16_le_encode("ab"))
s("c_utf16be_bmp", lambda: _codecs.utf_16_be_encode("ab"))
s("c_utf32_bmp", lambda: _codecs.utf_32_encode("ab"))
s("c_utf32le_bmp", lambda: _codecs.utf_32_le_encode("ab"))
s("c_utf32be_bmp", lambda: _codecs.utf_32_be_encode("ab"))
s("c_ascii_bmp", lambda: _codecs.ascii_encode("ab"))
s("c_ascii_strict", lambda: _codecs.ascii_encode(EAC))
s("c_latin1_eacute", lambda: _codecs.latin_1_encode(EAC))
s("c_latin1_strict", lambda: _codecs.latin_1_encode(A))
s("c_utf8_lone", lambda: _codecs.utf_8_encode(LONE_HI))
s("c_utf8_too_many", lambda: _codecs.utf_8_encode("ab", "strict", "x"))
s("c_utf8_not_str", lambda: _codecs.utf_8_encode(b"ab"))
s("c_generic_encode", lambda: _codecs.encode("ab", "utf-16-le"))
s("c_generic_encode_default", lambda: _codecs.encode(EAC))
s("c_decode_bmp", lambda: _codecs.utf_8_decode(b"ab"))
s("c_decode_eacute", lambda: _codecs.utf_8_decode(EAC.encode("utf-8")))
