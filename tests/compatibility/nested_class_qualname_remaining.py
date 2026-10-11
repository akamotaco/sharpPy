"""Compiler metadata difference discovered during explicit type repr checks."""
class Outer:
    class Inner: pass
print(Outer.Inner.__qualname__)
print(type.__repr__(Outer.Inner))
