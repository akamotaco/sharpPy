def show(label, fn):
    try:
        r = fn()
        print(label, repr(r))
    except Exception as e:
        print(label, type(e).__name__ + ":", e)

# pow zero negative
show("pz1", lambda: 0 ** -1)
show("pz2", lambda: 0.0 ** -1)
show("pz3", lambda: 0 ** -1.0)
show("pz4", lambda: 2 ** -1)
show("pz5", lambda: (-2) ** -1)
show("pz6", lambda: 0.0 ** -2)
show("pz7", lambda: pow(0, -1))
# bit_length
show("bl", lambda: [(-5).bit_length(), 0 .bit_length(), (1 << 63).bit_length(), (1 << 64).bit_length(), (2**1000 - 1).bit_length()])
# round float
show("rf", lambda: [round(0.5), round(1.5), round(2.5), round(-2.5), round(2.675, 2)])
show("rf2", lambda: round(1234.5678, -2))
show("rf3", lambda: round(-1250.0, -3))
show("rf4", lambda: [round(1.0, 400) == 1.0, round(2.5000000000000004, 1)])
show("rf5", lambda: round(float("inf")))
show("rf6", lambda: round(float("nan")))
show("rf7", lambda: round(1.5, 2.0))
show("rf8", lambda: round(1.0, 10 ** 18))
show("rf9", lambda: round(1e307, -10))
# round int
show("ri", lambda: [round(1234, -2), round(1250, -2), round(1350, -2), round(-1250, -2), round(-1350, -2), round(1234), round(1234, 2)])
show("ri2", lambda: round(123456789012345678901234567890, -20))
show("ri3", lambda: round(1234, 2.0))
# float.hex / fromhex
show("hx", lambda: [float.hex(0.1), (-0.1).hex(), (1.0).hex(), (-0.0).hex(), (0.0).hex(), (5e-324).hex(), (3.14159).hex()])
show("hx2", lambda: [float.fromhex('0x1.999999999999ap-4'), (5e-324).fromhex((5e-324).hex())])
show("hx3", lambda: [float('inf').hex(), float('-inf').hex(), float('nan').hex()])
# str/repr floats
show("sr", lambda: [str(1e16) == repr(1e16), str(1e16), repr(1e300), str(0.1), str(-0.0), str(1e-7), str(123456789012345678901234567890.0)])
# -0.0
show("nz", lambda: [repr(-0.0), -0.0 == 0, str(-0.0)])
# float(int) overflow
show("fi", lambda: float(10 ** 400) == float('inf'))
show("fi2", lambda: float(10 ** 308))
show("fi3", lambda: float(-(10 ** 400)))
show("fi4", lambda: float(2 ** 1023))
show("fi5", lambda: 10 ** 400 / 1 == float('inf'))
