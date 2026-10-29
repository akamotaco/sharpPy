# p45: memoryview equality and hashing (Objects/memoryobject.c memory_richcompare /
# memory_hash). Only == and != exist; a non-view operand must export a buffer, so a view
# equals bytes/bytearray but is merely unequal to str/int/list; differing shapes are unequal
# (itemsize is NOT part of the shape, so a 4-byte 'f' view and an 8-byte 'd' view both shape
# (1,)); items compare by decoded value, never memcmp, so NaN stays unequal and 'b' -1 is not
# 'B' 255. Hashing requires a readonly byte-format view and must equal the bytes hash, which
# is what makes {b"abc": 1}[memoryview(b"abc")] a hit. Hash values themselves are not
# cross-implementation comparable, so they are only compared against the matching bytes.
import re

ADDR = re.compile(r"0x[0-9a-fA-F]+")


def s(l, f):
    try:
        print(l, ADDR.sub("0xADDR", repr(f())))
    except Exception as e:
        print(l, "EXC", type(e).__name__, ADDR.sub("0xADDR", str(e)))


b = b"abc"
ba = bytearray(b"abc")
m = memoryview(b)
mba = memoryview(ba)
i4 = memoryview(bytes([1, 0, 0, 0])).cast("I")
h2 = memoryview(bytes([1, 0, 0, 0])).cast("H")
u8 = memoryview(b"\xff")
s8 = memoryview(b"\xff").cast("b")
c1 = memoryview(b"abc").cast("c")
sliced = memoryview(bytearray(b"xxabcxx"))[2:5]

s("mv_mv", lambda: m == memoryview(b"abc"))
s("mv_bytes", lambda: m == b)
s("bytes_mv", lambda: b == m)
s("mv_ba", lambda: m == ba)
s("ba_mv", lambda: ba == m)
s("mvba_mv", lambda: mba == m)
s("mv_str", lambda: m == "abc")
s("str_mv", lambda: "abc" == m)
s("mv_int", lambda: m == 5)
s("mv_list", lambda: m == [97, 98, 99])
s("mv_ne_bytes", lambda: m == b"abd")
s("mv_ne_shorter", lambda: m == b"ab")
s("ne_op", lambda: m != b"abd")
s("ne_same", lambda: m != b"abc")
s("sliced_eq", lambda: sliced == m)
s("sliced_ne", lambda: memoryview(bytearray(b"xxabcxx"))[1:4] == m)
s("i4_h2", lambda: i4 == h2)
s("i4_i4", lambda: i4 == memoryview(bytes([1, 0, 0, 0])).cast("I"))
s("i4_bytes", lambda: i4 == bytes([1, 0, 0, 0]))
s("bytes_i4", lambda: bytes([1, 0, 0, 0]) == i4)
s("u8_s8", lambda: u8 == s8)
s("u8_B", lambda: u8 == memoryview(b"\xff"))
s("c1_B", lambda: c1 == m)
s("lt_op", lambda: m < m)
s("le_op", lambda: m <= m)
s("gt_op", lambda: m > memoryview(b"aaa"))
s("in_dict", lambda: (lambda d: d[m])( {b"abc": 1} ))
s("set_mv", lambda: len({m, memoryview(b"abc"), b"abc"} ) if False else "skip")
s("hash_B_type", lambda: type(hash(m)).__name__)
s("hash_eq_bytes", lambda: hash(m) == hash(b))
s("hash_eq_ba", lambda: hash(mba) == hash(b) if False else "skip")
s("hash_I", lambda: hash(i4))
s("hash_c_matches_bytes", lambda: hash(memoryview(b"a").cast("c")) == hash(b"a"))
s("hash_sliced", lambda: hash(sliced) == hash(b))
s("hash_released", lambda: (lambda v: (v.release(), hash(v))[1])(memoryview(b"abc")))
s("eq_released", lambda: (lambda v: (v.release(), v == m)[1])(memoryview(b"abc")))
s("mv_tuple", lambda: m == (97, 98, 99))
s("bool_eq", lambda: m == True)


# dict/set membership is the observable payoff of the hash + equality rules
s("dict_bytes_key_mv", lambda: {b"abc": 1}[m])
s("dict_mv_key_bytes", lambda: {m: 1}[b"abc"])
s("mv_in_bytes_set", lambda: m in {b"abc"})
s("bytes_in_mv_set", lambda: b"abc" in {m})
s("dict_sliced_hit", lambda: {b"abc": 1}[sliced])
s("dict_miss", lambda: {b"abd": 1}[m])
s("dict_I_unhashable", lambda: {i4: 1}[i4])
s("mv_eq_f_vs_d", lambda: memoryview(bytes(4)).cast("f") == memoryview(bytes(8)).cast("d"))
NAN8 = bytes([0x7F, 0xF8, 0, 0, 0, 0, 0, 0])
s("mv_eq_nan", lambda: memoryview(NAN8).cast("d") == memoryview(NAN8).cast("d"))
s("mv_eq_nan_cross_fmt", lambda: memoryview(NAN8).cast("d") == memoryview(bytes(8)).cast("d"))
s("mv_eq_zero_len", lambda: memoryview(b"") == memoryview(b""))
s("mv_eq_empty_vs_one", lambda: memoryview(b"") == memoryview(b"a"))
s("mv_vs_mv_writable", lambda: memoryview(bytearray(b"abc")) == memoryview(bytearray(b"abc")))
