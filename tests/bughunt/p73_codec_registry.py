#!/usr/bin/env python3
"""
p73: the codec registry.

CPython resolves an encoding name through a registry: _PyCodec_Lookup in Python/codecs.c
normalizes the name, walks the registered search functions, and caches the CodecInfo it gets
back. codecs.lookup / codecs.getencoder / codecs.encode / the encodings package are all clients
of that one lookup, and codecs.CodecInfo is a tuple subclass whose two halves are codec
functions of the shape f(data, errors) -> (result, consumed).

Before this probe, this interpreter had a hard-coded table: codecs.lookup was missing, so
codecs.encode(b'ab', 'hex') and codecs.decode('ab', 'rot13') - both answered by CPython - were
TypeErrors, and 'ab'.encode('rot13') reported 'unknown encoding: rot13' where CPython says the
name is a codec but not a text encoding.

Deliberately NOT pinned here, because they stay open in docs/known_gaps.md: the repr of a
CodecInfo (this interpreter does not honour __new__ of a tuple subclass), the per-codec
StreamReader/StreamWriter/Incremental* classes, the codecs this interpreter has no implementation
for (cp1252, big5, shift_jis, koi8-r, quopri, uu, punycode, idna, utf_7, ...), the name of
binascii.Error, and the _codecs functions of the codecs this interpreter lacks.
"""
import codecs
import sys


def show(label, fn):
    try:
        value = fn()
    except BaseException as exc:
        value = f"{type(exc).__name__}: {exc}"
    print(f"{label:<52} -> {ascii(value)}")


print("=== the caller's spelling survives into the failure ===")
show("unknown encoding", lambda: codecs.lookup("totally-made-up"))
show("unknown encoding keeps hyphens", lambda: codecs.lookup("Utf-Made-Up"))
show("lookup(None)", lambda: codecs.lookup(None))
show("lookup(5)", lambda: codecs.lookup(5))

print("=== canonical names, which are not the alias ===")
for name in ["utf-8", "UTF8", "utf_8", "u8", "UTF", "utf",
             "latin1", "LATIN1", "latin-1", "iso_8859-1", "8859",
             "ascii", "us-ascii", "646",
             "utf-16", "utf_16", "utf-16le", "utf_16_le", "utf-16-be", "utf-16  ",
             "utf-32", "utf_32_le", "utf-32-be",
             "utf-8-sig", "utf8mb4", "cp1252-x"]:
    show(f"lookup({name!r}).name", lambda n=name: codecs.lookup(n).name)

print("=== CodecInfo is a tuple of two codec functions ===")
ci = codecs.lookup("utf-8")
show("isinstance(ci, tuple)", lambda: isinstance(ci, tuple))
show("len(ci)", lambda: len(ci))
show("ci.name", lambda: ci.name)
show("ci.encode('ab')", lambda: ci.encode("ab"))
show("ci.decode(b'ab')", lambda: ci.decode(b"ab"))
show("codecs.CodecInfo is the class", lambda: isinstance(ci, codecs.CodecInfo))
show("ci.errors attribute", lambda: ci.errors)
show("codecs.encode is _codecs.encode", lambda: __import__("_codecs").encode is codecs.encode)

print("=== getencoder / getdecoder are the halves of the lookup ===")
show("getencoder('utf-8')('ab')", lambda: codecs.getencoder("utf-8")("ab"))
show("getdecoder('utf-8')(b'ab')", lambda: codecs.getdecoder(b"ab" and b"ab"))
show("getdecoder('latin-1')(b'\\xe9', 'strict')",
     lambda: codecs.getdecoder("latin-1")(b"\xe9", "strict"))
show("getencoder('utf-16')('ab')", lambda: codecs.getencoder("utf-16")("ab"))
show("getencoder('cp1252')('ab')", lambda: codecs.getencoder("made-up")("ab"))
show("getdecoder(5)", lambda: codecs.getdecoder(5))

print("=== roundtrips through the registry halves ===")
for encoding in ["utf-8", "utf-16", "utf-16-le", "utf-16-be", "utf-32", "utf-32-le",
                 "ascii", "iso8859-1"]:
    text = "hi"
    enc = codecs.getencoder(encoding)
    dec = codecs.getdecoder(encoding)
    produced = enc(text)[0]
    show(f"{encoding}: encode then decode", lambda p=produced, d=dec: d(p)[0] == text)
    show(f"{encoding}: bytes of {text!r}", lambda p=produced: p)

print("=== non-text codecs are codecs, just not text encodings ===")
for name in ["rot13", "rot-13", "hex", "base64"]:
    show(f"lookup({name!r}).name", lambda n=name: codecs.lookup(n).name)
    show(f"lookup({name!r})._is_text_encoding",
         lambda n=name: getattr(codecs.lookup(n), "_is_text_encoding", "missing"))
show("codecs.encode(b'ab','hex')", lambda: codecs.encode(b"ab", "hex"))
show("codecs.decode(b'6162','hex')", lambda: codecs.decode(b"6162", "hex"))
show("codecs.encode('ab','rot13')", lambda: codecs.encode("ab", "rot13"))
show("codecs.decode('no','rot13')", lambda: codecs.decode("no", "rot13"))
show("codecs.encode(b'ab','base64')", lambda: codecs.encode(b"ab", "base64"))
show("codecs.decode(b'YWI=','base64')", lambda: codecs.decode(b"YWI=", "base64"))
show("hex of 0xab bytes", lambda: codecs.encode(b"\xab\xcd", "hex"))
show("hex of empty", lambda: codecs.encode(b"", "hex"))
show("base64 of empty", lambda: codecs.encode(b"", "base64"))
show("base64 roundtrip of long", lambda: codecs.decode(
    codecs.encode(bytes(range(200)), "base64"), "base64") == bytes(range(200)))

print("=== a non-text codec is refused by name on the text paths ===")
show("'ab'.encode('rot13')", lambda: "ab".encode("rot13"))
show("'ab'.encode('hex')", lambda: "ab".encode("hex"))
show("b'ab'.decode('rot13')", lambda: b"ab".decode("rot13"))
show("str(b'ab', 'base64')", lambda: str(b"ab", "base64"))
show("'ab'.encode('made-up')", lambda: "ab".encode("made-up"))

print("=== register and unregister ===")
seen = []


def search(name):
    seen.append(name)
    if name == "mycodec":
        return codecs.CodecInfo(codecs.utf_8_encode, codecs.utf_8_decode, name="mycodec")
    return None


show("register(1)", lambda: codecs.register(1))
show("register()", lambda: codecs.register())
show("register(search) -> None", lambda: codecs.register(search))
show("lookup('MYCODEC').name", lambda: codecs.lookup("MYCODEC").name)
show("lookup('my_codec').name", lambda: codecs.lookup("my_codec").name)
show("search saw normalized names", lambda: sorted(set(seen)))
show("registered codec encodes", lambda: codecs.lookup("mycodec").encode("é"))
show("codecs.encode with registered name", lambda: codecs.encode("é", "mycodec"))
show("unregister(search) -> None", lambda: codecs.unregister(search))
show("lookup('mycodec') after unregister", lambda: codecs.lookup("mycodec"))
show("search fn may return a non-codec", lambda: codecs.register(lambda n: 42) or codecs.lookup("anything"))
codecs.unregister(codecs.lookup)  # remove the toy search again
show("unknown again after cleanup", lambda: codecs.lookup("anything"))

print("=== the registry answers str.encode and bytes.decode too ===")
show("'ab'.encode('UTF-16')", lambda: "ab".encode("UTF-16"))
show("b'\\xff\\xfea\\x00'.decode('utf-16')", lambda: b"\xff\xfea\x00".decode("utf-16"))
show("'\\u00e9'.encode('LATIN1')", lambda: "\u00e9".encode("LATIN1"))
show("b'\\xe9'.decode('ISO_8859-1')", lambda: b"\xe9".decode("ISO_8859-1"))
show("'\\u00e9'.encode('utf-8','strict')", lambda: "\u00e9".encode("utf-8", "strict"))
show("errors= through the lookup path", lambda: "a\u00e9".encode("ascii", "backslashreplace"))

print("=== module surface ===")
show("codecs.lookup exists", lambda: callable(codecs.lookup))
show("_codecs.lookup exists", lambda: callable(__import__("_codecs").lookup))
show("codecs.unregister exists", lambda: callable(codecs.unregister))
show("codecs.rot13 missing from codecs", lambda: hasattr(codecs, "rot13_encode"))
print("DONE")
