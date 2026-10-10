class Base:
    def value(self):
        return 7
class Derived(Base):
    def value(self):
        capture = lambda: self
        factory = super
        assert capture() is self
        return factory().value()
assert Derived().value() == 7
print('super captured first argument passed')
