# p35: int(float) uses arbitrary-precision truncation (was clamped to Int32).
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


show("int-1e17", lambda: int(1e17))
show("int-1e9", lambda: int(1e9))
show("int-2p40", lambda: int(2.0 ** 40))
show("int-1e30", lambda: int(1e30))
show("int-2.5", lambda: int(2.5))
show("int-neg-2.7", lambda: int(-2.7))
show("int-neg-0.9", lambda: int(-0.9))
show("int-0.0", lambda: int(0.0))
show("int-1.5-eq1", lambda: int(1.5) == 1)
show("int-pi", lambda: int(3.1415926535897931))
show("int-inf", lambda: int(float("inf")))
show("int-ninf", lambda: int(float("-inf")))
show("int-nan", lambda: int(float("nan")))
show("int-1e18", lambda: int(1e18))
show("float-repr-roundtrip", lambda: int(2 ** 53 + 2.0))
