import base64
import binascii
import struct

print(struct.calcsize("i"), struct.calcsize("<i"), struct.calcsize("<ihb"), struct.calcsize(">qd"), struct.calcsize("!3s2h"))
print(struct.calcsize("@bi"), struct.calcsize("=bi"), struct.calcsize("@bq"), struct.calcsize("<bq"), struct.calcsize("5x"))
for fmt, values in [("<i", (1,)), (">i", (1,)), ("!h", (-2,)), ("<BHI", (1, 2, 3)), ("<qQ", (-1, 2**64 - 1)),
                    ("<f", (1.5,)), (">d", (-2.25,)), ("<e", (0.5,)), ("<?", (True,)), ("<3s", (b"ab",)),
                    ("<2s", (b"abc",)), ("<c", (b"x",)), ("<p", (b"",)), ("<5p", (b"abc",)), ("<x", ()), ("<2i", (1, 2)),
                    ("3B", (1, 2, 3)), ("<b b", (1, 2)), ("<i2xh", (1, 2)), ("@ih", (1, 2)), ("<n", ()) if False else ("<H", (65535,))]:
    packed = struct.pack(fmt, *values)
    print(fmt, packed, struct.unpack(fmt, packed), len(packed) == struct.calcsize(fmt))
print(struct.pack(b"<i", 7), struct.unpack("<f", b"\x00\x00\x80\x3f"), struct.unpack("<d", bytes(8)))
s = struct.Struct("<ih")
print(s.format, s.size, s.pack(1, 2), s.unpack(b"\x01\x00\x00\x00\x02\x00"), s.unpack_from(b"\x00\x01\x00\x00\x00\x02\x00", 1))
buf = bytearray(8)
struct.pack_into("<h", buf, 2, 513)
print(buf, struct.unpack_from("<h", buf, 2), struct.unpack_from("<h", bytes(buf), offset=2))
s.pack_into(buf, 1, 1, 2)
print(buf, list(struct.iter_unpack("<h", b"\x01\x00\x02\x00")), list(s.iter_unpack(bytes(6))))
for call in (lambda: struct.pack("<b", 200), lambda: struct.pack("<H", -1), lambda: struct.pack("<i", "x"),
             lambda: struct.pack("<i"), lambda: struct.pack("<i", 1, 2), lambda: struct.unpack("<i", b"a"),
             lambda: struct.unpack_from("<i", b"ab"), lambda: struct.pack("<f", "x"), lambda: struct.calcsize("y"),
             lambda: struct.calcsize("3"), lambda: list(struct.iter_unpack("<i", b"abc")), lambda: struct.pack("<3s", 1)):
    try:
        print(call())
    except struct.error as e:
        print("error", e)
print(issubclass(struct.error, Exception), struct.error.__name__)

print(base64.b64encode(b"hello world"), base64.b64decode(b"aGVsbG8gd29ybGQ="), base64.b64decode("aGk="))
print(base64.urlsafe_b64encode(b"\xfb\xff"), base64.urlsafe_b64decode(b"-_8="), base64.standard_b64encode(b"a"))
print(base64.b32encode(b"hi"), base64.b32decode(b"NBUQ===="), base64.b16encode(b"\xab"), base64.b16decode(b"AB"))
print(base64.b85encode(b"hello"), base64.b85decode(b"Xk~0{Zv"), base64.a85encode(b"hello"), base64.a85decode(b"BOu!rDZ"))
print(base64.encodebytes(b"hello"), base64.decodebytes(b"aGVsbG8=\n"))
print(binascii.hexlify(b"\x01\xab"), binascii.unhexlify("01ab"), binascii.crc32(b"hello"), binascii.b2a_base64(b"hi"), binascii.a2b_base64(b"aGk="))
print(b"\x01\xab".hex(), bytes.fromhex("01ab"))
try:
    base64.b64decode(b"a", validate=True)
except binascii.Error as e:
    print("error", e)
