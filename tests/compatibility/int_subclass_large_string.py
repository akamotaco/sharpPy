"""Pending: large decimal string parsing before int-subclass construction."""
class Code(int): pass
for value in (10 ** 80, -(10 ** 80)):
    print(Code(str(value)))
