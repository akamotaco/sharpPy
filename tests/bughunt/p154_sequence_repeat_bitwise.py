print(2 * "a", 2 * b"a", 2 * [1], 2 * (1,), True * "a", (1 << 128) & 255, 3 & (1 << 70), (1 << 70) ^ 5, 5 ^ (1 << 70), (1 << 70) | 1, 1 | (1 << 70))
class I(int): pass
print(I(2) * "a", "a" * I(2), I(6) & 3, 6 & I(3), I(6) ^ 3, 6 ^ I(3), I(6) | 1, 1 | I(6), I(1) << 2, 1 << I(2), I(4) >> 1, ~I(1), -I(1), abs(I(-1)), [1,2][I(1)], "ab"[I(1)], range(I(2)), I(2) ** 2, 2 ** I(2), I(7) // 2, 7 // I(2), I(7) % 2, 7 % I(2))
import enum
class F(enum.IntFlag):
    A = 1
print(F.A & 1, 1 & F.A, F.A | 2, 2 ^ F.A, 3 * "a" if False else "")
class E(enum.IntEnum):
    X = 2
print(E.X * "a", "a" * E.X, E.X * [1], E.X + 1, 1 + E.X, E.X & 3, 3 & E.X)
print(True & 1, 1 & True, True ^ 3, 3 | False, True << 2)
