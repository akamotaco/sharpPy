def s(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e)[:64])


FB = int.from_bytes

s("fb_wide", lambda: FB((2 ** 70).to_bytes(10, "big"), "big"))
s("fb_ff_unsigned", lambda: FB(b"\xff", "big"))
s("fb_ff_signed", lambda: FB(b"\xff", "big", signed=True))
s("fb_80_signed", lambda: FB(b"\x80", "big", signed=True))
s("fb_80_unsigned", lambda: FB(b"\x80", "big"))
s("fb_little", lambda: FB(b"\x01\x02", "little"))
s("fb_empty", lambda: FB(b"", "big"))
s("fb_empty_signed", lambda: FB(b"", "little", signed=True))
s("fb_bytearray", lambda: FB(bytearray(b"\x01\xff"), "big"))
s("fb_list", lambda: FB([1, 2, 3], "big"))
s("fb_tuple", lambda: FB((255, 0), "little"))
s("fb_range", lambda: FB(range(3), "big"))
s("fb_mv", lambda: FB(memoryview(b"\x01\x02"), "big"))
s("fb_str", lambda: FB("ab", "big"))
s("fb_bad_elem", lambda: FB([1, "x"], "big"))
s("fb_256", lambda: FB([256], "big"))
s("fb_neg_elem", lambda: FB([-1], "big"))
s("fb_bad_order", lambda: FB(b"a", "middle"))
s("fb_kw_order", lambda: FB(b"\x01\x00", byteorder="little"))
s("fb_neg_wide", lambda: FB((-2 ** 70).to_bytes(10, "big", signed=True), "big", signed=True))
s("fb_int_subclass_elem", lambda: FB([type("I", (int,), {})(7), 0], "big"))

s("tb_wide", lambda: (2 ** 70).to_bytes(10, "big").hex())
s("tb_wide_neg", lambda: (-2 ** 70).to_bytes(10, "big", signed=True).hex())
s("tb_2_200", lambda: (2 ** 200).to_bytes(26, "little").hex())
s("tb_min_default", lambda: (5).to_bytes().hex())
s("tb_zero_len", lambda: (0).to_bytes(0, "big").hex())
s("tb_zero_len_signed", lambda: (0).to_bytes(0, "big", signed=True).hex())
s("tb_too_short", lambda: (2 ** 70).to_bytes(5, "big"))
s("tb_signed_128", lambda: (128).to_bytes(1, "big", signed=True))
s("tb_signed_127", lambda: (127).to_bytes(1, "big", signed=True).hex())
s("tb_signed_neg128", lambda: (-128).to_bytes(1, "big", signed=True).hex())
s("tb_signed_neg129", lambda: (-129).to_bytes(1, "big", signed=True))
s("tb_neg_unsigned", lambda: (-1).to_bytes(1, "big"))
s("tb_neg_len", lambda: (1).to_bytes(-1, "big"))
s("tb_int_subclass", lambda: type("J", (int,), {})(2 ** 70).to_bytes(10, "little").hex())


def roundtrip_unsigned():
    return all(FB(n.to_bytes(9, "little"), "little") == n for n in range(0, 300))


def roundtrip_signed():
    return all(FB(n.to_bytes(9, "little", signed=True), "little", signed=True) == n
               for n in range(-300, 300))


s("roundtrip_unsigned", roundtrip_unsigned)
s("roundtrip_signed", roundtrip_signed)


def wide_roundtrip():
    for n in (2 ** 63, 2 ** 64, 2 ** 65 - 1, 2 ** 127, 2 ** 200 + 7):
        for order in ("big", "little"):
            if FB(n.to_bytes(32, order), order) != n:
                return ("mismatch", n, order)
    return "ok"


s("wide_roundtrip", wide_roundtrip)
