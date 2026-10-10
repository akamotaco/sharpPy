class Base:
    def value(self):
        return 7
class Derived(Base):
    def alias(self):
        factory = super
        return factory().value()
    def container(self):
        return [super][0]().value()
    def loop(self):
        for factory in [super]:
            return factory().value()
    def nested(self):
        def inner(receiver):
            factory = super
            return factory().value()
        return inner(self)
    lambda_value = lambda self: super().value()
    def shadowed(self, super):
        return super
    def stored(self):
        super = 13
        return super
    def assigned_only(self):
        super = 13
        return 1
    def explicit(self):
        return super(Derived, self).value()
    def generator(self):
        factory = super
        yield factory().value()

obj = Derived()
assert obj.alias() == 7
assert obj.container() == 7
assert obj.loop() == 7
assert obj.nested() == 7
assert obj.lambda_value() == 7
assert obj.explicit() == 7
assert list(obj.generator()) == [7]
assert obj.shadowed(11) == 11
assert obj.stored() == 13
for method in [Derived.alias, Derived.container, Derived.loop, Derived.nested,
               Derived.lambda_value, Derived.shadowed, Derived.stored,
               Derived.explicit, Derived.generator]:
    assert method.__code__.co_freevars == ('__class__',), method.__name__
assert Derived.assigned_only.__closure__ is None

class GlobalClass:
    def name(self):
        global __class__
        return super
assert GlobalClass.name.__closure__ is None
assert GlobalClass().name() is super

def outside(value):
    return super
assert outside.__closure__ is None
assert outside(None) is super

class BodyOnly:
    factory = super
    def plain(self):
        return 1
assert BodyOnly.plain.__closure__ is None
print('super name analysis regression passed')

class SuppressedCell:
    def local_class(self):
        __class__ = None
        return super()
    def global_class(self):
        global __class__
        return super()
assert SuppressedCell.local_class.__closure__ is None
assert SuppressedCell.global_class.__closure__ is None

def outside_call(value):
    return super()
assert outside_call.__closure__ is None
print('super cell scope suppression passed')
