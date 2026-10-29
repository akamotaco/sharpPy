# p33: memoryview.cast / iteration + re flags (unblocked by cast).
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# --- memoryview.cast (native little-endian reinterpretation) ---
show("itemsize", lambda: memoryview(b"abcdefgh").cast('I').itemsize)
show("len", lambda: len(memoryview(b"abcdefgh").cast('I')))
show("tolist", lambda: memoryview(b"\x01\x00\x00\x00\xff\x00\x00\x00").cast('I').tolist())
show("index0", lambda: memoryview(b"\x01\x00\x00\x00").cast('I')[0])
show("neg-index", lambda: memoryview(b"\x01\x00\x00\x00\x02\x00\x00\x00").cast('I')[-1])
show("iter-cast", lambda: list(memoryview(b"\x01\x00\x00\x00\x02\x00\x00\x00").cast('I')))
show("iter-bytes", lambda: list(memoryview(b"ab")))
show("cast-B", lambda: memoryview(b"ab").cast('B').tolist())
show("cast-H", lambda: memoryview(b"\x01\x00\x02\x00").cast('H').tolist())
show("cast-signed-h", lambda: memoryview(b"\xff\xff").cast('h').tolist())
show("cast-i", lambda: memoryview(b"\xff\xff\xff\xff").cast('i').tolist())
show("cast-shape", lambda: memoryview(b"abcdefgh").cast('I', (2,)).tolist())
show("nbytes", lambda: memoryview(b"abcd").cast('H').nbytes)
show("ndim", lambda: memoryview(b"abcd").cast('H').ndim)
show("format", lambda: memoryview(b"abcd").cast('H').format)
show("format-bytes", lambda: memoryview(b"ab").format)
show("itemsize-bytes", lambda: memoryview(b"ab").itemsize)
show("misaligned", lambda: memoryview(b"abc").cast('I'))
show("shape-bad", lambda: memoryview(b"abcd").cast('I', (2,)))
show("empty-cast", lambda: len(memoryview(b"").cast('I')))
show("bytearray-cast", lambda: memoryview(bytearray(b"\x05\x00\x00\x00")).cast('I').tolist())
show("cast-then-tobytes", lambda: memoryview(b"\x01\x02\x03\x04").cast('I').tobytes())
show("bad-format", lambda: memoryview(b"ab").cast('Z'))


# --- re flags (were blocked by the memoryview.cast crash in Lib/re/_compiler) ---
import re
show("ignorecase-findall", lambda: re.findall(r"[a-z]+", "AbCd", flags=re.IGNORECASE))
show("ignorecase-match", lambda: re.match(r"HELLO", "hello", re.IGNORECASE).group(0))
show("multiline", lambda: re.findall(r"^x", "x\nx", re.MULTILINE))
show("dotall", lambda: re.findall(r"a.b", "a\nb", re.DOTALL))
show("verbose", lambda: re.findall(r"a b", "a  b", re.VERBOSE))
show("ci-sub", lambda: re.sub(r"cat", "dog", "CAT cat", flags=re.IGNORECASE))
show("named-ci", lambda: re.match(r"(?P<y>[A-Z]+)", "abc", re.IGNORECASE).group("y"))
