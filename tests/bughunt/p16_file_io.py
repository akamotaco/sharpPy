# p16: file I/O semantics (text/binary, modes, positioning, errors)
import os
import sys

# Scratch files live outside the repo root (Bughunt hygiene rule 2026-10-28):
# the bughunt harness injects one shared directory so both interpreters observe
# identical absolute paths in error text.
# The fallback used to be ".", which left p16a.txt/p16b.bin in the repo root whenever the probe was
# run directly instead of through the harness (observed 2026-10-29). The temp directory comes from the
# environment rather than tempfile.gettempdir(), because SharpPy has no `tempfile` module yet: a
# direct `import tempfile` here aborts the whole probe. Both interpreters read the same variables, so
# the path text still compares, and the harness still overrides with one shared directory.
tmp_base = (os.environ.get("BUGHUNT_SCRATCH")
            or os.environ.get("TMPDIR")
            or os.environ.get("TMP")
            or os.environ.get("TEMP")
            or ".").replace("\\", "/")
tmp = tmp_base + "/p16a.txt"
tmp2 = tmp_base + "/p16b.bin"
_missing = tmp_base + "/p16_no_such_9876"
_missing2 = tmp_base + "/p16_no_such_file_12345"

for _name in tuple(tmp + s for s in ("", "x", "p", "w", "n", "n2", "n3", "u", "bad", "l", "t", "r", "a1", "b1", "wl", "wl2")) + (tmp2, tmp2 + "x"):
    try:
        os.remove(_name)
    except OSError:
        pass

def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

# --- basic text roundtrip ---
def write_text():
    f = open(tmp, "w")
    n = f.write("hello\nworld\n")
    f.close()
    return n
show("write.count", write_text)
show("read.all", lambda: open(tmp).read())
show("read.reopen.read", lambda: (lambda f: (f.read(), f.read()))(open(tmp)))
show("readlines", lambda: open(tmp, "r").readlines())
show("iterate", lambda: [ln for ln in open(tmp)])
show("read.size", lambda: open(tmp).read(5))
show("readline", lambda: (lambda f: (f.readline(), f.readline(), f.readline(), f.readline()))(open(tmp)))

# --- append ---
def append_mode():
    with open(tmp, "a") as f:
        f.write("more\n")
    return open(tmp).read()
show("append.roundtrip", append_mode)

# --- binary ---
def bin_roundtrip():
    with open(tmp2, "wb") as f:
        written = f.write(b"\x00\x01\xff")
    with open(tmp2, "rb") as f:
        data = f.read()
    return (written, data, len(data))
show("bin.roundtrip", bin_roundtrip)
show("bin.seek0", lambda: (lambda f: (f.read(1), f.seek(0), f.read(2), f.tell()))(open(tmp2, "rb")))

# --- tell/seek ---
def pos_ops():
    f = open(tmp)
    first = f.read(5)
    p = f.tell()
    f.seek(0)
    second = f.read(5)
    f.close()
    return (first, p, second)
show("pos.text", pos_ops)
show("seek.end", lambda: (lambda f: (f.seek(0, 2), f.tell()))(open(tmp2, "rb")))
show("seek.cur", lambda: (lambda f: (f.read(1), f.seek(1, 1), f.read(1))) (open(tmp2, "rb")))
show("seek.neg.exc", lambda: (lambda f: f.seek(-1))(open(tmp2, "rb")))
show("whence.bad", lambda: (lambda f: f.seek(0, 5))(open(tmp2, "rb")))

# --- modes ---
show("mode.bad", lambda: open(tmp, "q"))
show("mode.empty", lambda: open(tmp, ""))
show("mode.both", lambda: open(tmp, "rbt"))
show("mode.x.new", lambda: (open(tmp2 + "x", "x").close(), os.path.exists(tmp2 + "x"))[1])
show("mode.x.dup", lambda: open(tmp2 + "x", "x"))
show("mode.r.wonly", lambda: open(tmp2, "w").read())
show("mode.w.r", lambda: open(tmp2, "r").write("nope"))
show("mode.plus.read", lambda: open(tmp, "r+").read(4))
def plus_write_read():
    with open(tmp + "p", "w+") as f:
        f.write("abc")
        f.seek(0)
        return f.read()
show("mode.wplus", plus_write_read)
show("missing.file", lambda: open(_missing2))
show("dir.open", lambda: open("."))

# --- closed semantics ---
def closed_ops():
    f = open(tmp)
    f.close()
    out = [f.closed]
    try:
        f.read()
        out.append("read-ok")
    except ValueError as ve:
        out.append(str(ve))
    f.close()  # double close must be fine
    return out
show("closed", closed_ops)

# --- with statement ---
def with_ctx():
    with open(tmp, "w") as f:
        f.write("ctx")
        exc_seen = None
    return (f.closed, open(tmp).read())
show("with.close", with_ctx)
def with_exc():
    try:
        with open(tmp + "w", "w") as f:
            raise RuntimeError("boom")
    except RuntimeError:
        pass
    return f.closed
show("with.exc", with_exc)

# --- newline translation (observable in binary) ---
def newline_check():
    with open(tmp + "n", "w") as f:
        f.write("a\nb\n")
    with open(tmp + "n", "rb") as f:
        raw = f.read()
    with open(tmp + "n") as f:
        text = f.read()
    return (raw, text)
show("newlines", newline_check)
def newline_param():
    with open(tmp + "n2", "w", newline="\r\n") as f:
        f.write("x\ny\n")
    with open(tmp + "n2", "rb") as f:
        return f.read()
show("newline.param", newline_param)
def newline_read():
    with open(tmp + "n3", "wb") as f:
        f.write(b"a\r\nb\rc\n")
    with open(tmp + "n3") as f:
        return f.read()
show("newlines.universal", newline_read)
def newline_read_keepends():
    with open(tmp + "n3") as f:
        return f.readlines()
show("newlines.readlines", newline_read_keepends)

# --- encoding ---
def utf8_round():
    with open(tmp + "u", "w", encoding="utf-8") as f:
        f.write("é한")
    with open(tmp + "u", "rb") as f:
        raw = f.read()
    with open(tmp + "u", encoding="utf-8") as f:
        s = f.read()
    return (len(raw), s)
show("encoding.utf8", utf8_round)
show("encoding.bad", lambda: open(tmp + "bad", encoding="no-such-codec"))
def latin1():
    with open(tmp + "l", "wb") as f:
        f.write(b"\xe9")
    with open(tmp + "l", encoding="latin-1") as f:
        return f.read()
show("encoding.latin1", latin1)

# --- truncate/flush ---
def trunc():
    with open(tmp + "t", "w") as f:
        f.write("abcdefgh")
        f.truncate(3)
    return open(tmp + "t").read()
show("truncate", trunc)

# --- os-level ---
def remove_round():
    with open(tmp + "r", "w") as f:
        f.write("x")
    os.remove(tmp + "r")
    return os.path.exists(tmp + "r")
show("os.remove", remove_round)
show("os.remove.missing", lambda: os.remove(_missing))
def rename_round():
    with open(tmp + "a1", "w") as f:
        f.write("data")
    os.rename(tmp + "a1", tmp + "b1")
    return (os.path.exists(tmp + "a1"), open(tmp + "b1").read())
show("os.rename", rename_round)

# --- file attrs ---
show("file.name", lambda: open(tmp).name.endswith("p16a.txt"))
show("file.mode", lambda: open(tmp, "rb").mode)
show("file.repr.type", lambda: type(open(tmp)).__name__)
show("file.writelines", lambda: (open(tmp + "wl", "w").writelines(["1\n", "2\n"]), open(tmp + "wl").read())[1])
show("writelines.mixed", lambda: open(tmp + "wl2", "w").writelines(iter(["a", "b"])))

# cleanup
for _name in tuple(tmp + s for s in ("", "x", "p", "w", "n", "n2", "n3", "u", "bad", "l", "t", "r", "a1", "b1", "wl", "wl2")) + (tmp2, tmp2 + "x"):
    try:
        os.remove(_name)
    except OSError:
        pass
print("cleanup done")
