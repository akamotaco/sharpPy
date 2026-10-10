class Base:
    def value(self):
        return 3
class Derived(Base):
    def value(self):
        factory = super
        return factory().value() + 1
assert Derived().value() == 4
print('super alias class cell passed')
