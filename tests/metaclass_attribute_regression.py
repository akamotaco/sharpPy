"""type_repr metadata and metatype data descriptor precedence (CPython 3.12)."""
class Outer:
    class Inner: pass
Outer.Inner.__qualname__ = 'Outer.Inner'

for module in ('__main__', 'custom', 'builtins', '', None, 42):
    Outer.Inner.__module__ = module
    print('repr', type.__repr__(Outer.Inner))

events = []
class Data:
    def __get__(self, instance, owner):
        events.append((instance.__name__, owner.__name__))
        return 'data'
    def __set__(self, instance, value): pass
class NonData:
    def __get__(self, instance, owner): return 'nondata'
class Meta(type):
    value = Data()
    other = NonData()
    @property
    def prop(cls): return 'property'
class DerivedMeta(Meta): pass
class Base(metaclass=DerivedMeta):
    value = 'local'
    other = 'local'
    prop = 'local'
class Child(Base): pass
print('precedence', Base.value, Child.value, Base.other, Child.prop)
print('events', events)
Meta.value = 'changed'
print('changed', Base.value, Child.value)
Meta.value = Data()
print('restored', Child.value)
for name in ('__setattr__', '__repr__'):
    print('native', name, name in type.__dict__, type.__dict__[name] is getattr(type, name))
class Failing(type):
    @property
    def value(cls): raise ValueError('descriptor error')
class Target(metaclass=Failing): value = 'hidden'
try:
    Target.value
except Exception as error:
    print(type(error).__name__, str(error))
