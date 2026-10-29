# p42: memoryview cast protocol and real N-D views.
# CPython memory_cast accepts one struct character with an optional '@' only
# ('=', '<', '>', '!' are rejected), takes format/shape keywords, validates shape
# elements, and produces genuinely N-D views. Sub-view forms are refused by CPython
# itself, so the exact wording is part of parity.
def s(l, f):
    try:
        print(l, repr(f()))
    except Exception as e:
        print(l, "EXC", type(e).__name__, str(e))


raw = bytes([1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0])
b16 = memoryview(raw)
m = memoryview(bytes([1, 0, 0, 0, 2, 0, 0, 0]))
v1 = b16.cast("I", (4,))
v2 = b16.cast("I", (2, 2))
v3 = b16.cast("B", (2, 2, 4))

# format grammar
for fmt in ["I", "@I", "=I", "<I", ">I", "!I", "bI", "2I", "", "Z", "II", "@", "@@B", " I", "i", "l", "q", "d", "f", "B"]:
    s(f"cast({fmt!r})", lambda fmt=fmt: m.cast(fmt).tolist())
for bad in [b"I", 73, None, [], 1.5, True, (1,), object()]:
    s(f"fmt_{type(bad).__name__}", lambda bad=bad: b16.cast(bad))

# arity / keywords
s("cast-0args", lambda: b16.cast().tolist())
s("cast-3args", lambda: b16.cast("I", (4,), 1).tolist())
s("cast-kw-format", lambda: b16.cast(format="I").tolist())
s("cast-kw-both", lambda: b16.cast(format="I", shape=(2, 2)).tolist())
s("cast-kw-order", lambda: b16.cast(shape=(4,), format="I").tolist())
s("cast-kw-extra", lambda: b16.cast("I", (4,), x=1).tolist())
s("cast-kw-dup", lambda: b16.cast("I", format="B").tolist())

# shape validation
s("shape-float", lambda: b16.cast("I", (2.0,)).tolist())
s("shape-str", lambda: b16.cast("I", ("2",)).tolist())
s("shape-empty", lambda: b16.cast("I", ()).tolist())
s("shape-zero", lambda: b16.cast("I", (0,)).tolist())
s("shape-neg", lambda: b16.cast("I", (-1,)).tolist())
s("shape-mismatch", lambda: b16.cast("I", (2, 3)).tolist())
s("shape-list", lambda: b16.cast("I", [2, 2]).tolist())
s("shape-str-container", lambda: b16.cast("I", "22").tolist())
s("shape-bool", lambda: b16.cast("I", (True, 4)).tolist())

# N-D view observables
s("v1.ndim", lambda: v1.ndim)
s("v1.shape", lambda: v1.shape)
s("v1.tolist", lambda: v1.tolist())
s("v1.strides", lambda: v1.strides)
s("v2.ndim", lambda: v2.ndim)
s("v2.shape", lambda: v2.shape)
s("v2.tolist", lambda: v2.tolist())
s("v2.strides", lambda: v2.strides)
s("v2.itemsize", lambda: v2.itemsize)
s("v2.nbytes", lambda: v2.nbytes)
s("v2.len", lambda: len(v2))
s("v3.tolist", lambda: v3.tolist())
s("v3.ndim", lambda: v3.ndim)
s("v3.strides", lambda: v3.strides)
s("v3.len", lambda: len(v3))

# indexing
s("i11", lambda: v2[1, 1])
s("i_01", lambda: v2[(0, 1)])
s("i_neg", lambda: v2[-1, -1])
s("i_oob", lambda: v2[0, 5])
s("i_oob1", lambda: v2[5, 0])
s("i_toofat", lambda: v2[0, 0, 0])
s("i_short", lambda: v2[(0,)])
s("i_empty", lambda: v2[()])
s("i_int", lambda: v2[0])
s("i3_int", lambda: v3[0])
s("i3_2", lambda: v3[0, 1])
s("i3_full", lambda: v3[0, 1, 3])
s("v1_int", lambda: v1[2])
s("v1_slice", lambda: v1[0:2].tolist())
s("v1_slice_ndim", lambda: v1[0:2].ndim)
s("v2_slice", lambda: v2[0:1].tolist())
s("v2_slice_ndim", lambda: v2[0:1].ndim)
s("v2_slice_shape", lambda: v2[0:1].shape)
s("v2_slice_len", lambda: len(v2[0:1]))
s("v2_slice_step", lambda: v2[::2].tolist())
s("v2_slice_strides", lambda: v2[::2].strides)
s("v2_slice_oob", lambda: v2[0:1][0, 1])

# buffer attributes
for obj, tag in [(b16, "bytes"), (v2, "nd"), (memoryview(bytearray(b"abcd")), "writable")]:
    s(f"c_contig_{tag}", lambda o=obj: o.c_contiguous)
    s(f"contig_{tag}", lambda o=obj: o.contiguous)
    s(f"readonly_{tag}", lambda o=obj: o.readonly)
    s(f"suboffsets_{tag}", lambda o=obj: o.suboffsets)
s("obj_is_bytes", lambda: b16.obj is raw)
s("obj_is_nd", lambda: v2.obj is raw)
s("c_contig_step", lambda: b16[::2].c_contiguous)
s("strides_slice", lambda: b16[0:2].strides)
s("strides_step", lambda: b16[::2].strides)

# materialized dunders (CPython slot wrappers)
s("has_getitem", lambda: hasattr(memoryview(b"a"), "__getitem__"))
s("has_setitem", lambda: hasattr(memoryview(b"a"), "__setitem__"))
s("has_len", lambda: hasattr(memoryview(b"a"), "__len__"))
s("unbound_getitem", lambda: type(b16).__getitem__(b16, 3))
s("unbound_len", lambda: type(b16).__len__(b16))
s("setitem_ro", lambda: b16.__setitem__(0, 1))
s("setitem_nd_ro", lambda: v2.__setitem__((0, 1), 1))
s("getitem_wrongself", lambda: type(b16).__getitem__("x", 0))
s("setitem_arity", lambda: memoryview(bytearray(b"ab")).__setitem__(0))

# writable N-D element assignment
w = memoryview(bytearray(8)).cast("H", (2, 2))
s("w_before", lambda: w.tolist())
s("w_write", lambda: (w.__setitem__((0, 1), 7), w.tolist())[1])
s("w_index", lambda: w[0, 1])
s("w_syntax", lambda: (w.__setitem__((1, 0), 9), w.tolist())[1])
