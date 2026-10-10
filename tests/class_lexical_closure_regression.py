class Base:
    def defining(self):
        return __class__
    def nested(self):
        def inner():
            return __class__
        return inner
    def factory(self):
        return lambda: __class__
    @staticmethod
    def static():
        return __class__
    @classmethod
    def class_method(cls):
        return __class__, cls
class Derived(Base):
    pass
assert Base().defining() is Base
assert Derived().defining() is Base
assert Derived().nested()() is Base
assert Derived().factory()() is Base
assert Derived.static() is Base
assert Derived.class_method() == (Base, Derived)
assert Derived().defining.__func__ is Base.defining
assert Derived.class_method.__func__ is Base.class_method.__func__

class Shadow:
    __class__ = 'namespace value'
    def defining(self):
        return __class__
assert Shadow.__dict__['__class__'] == 'namespace value'
assert Shadow().defining() is Shadow

__class__ = 'module value'
class LocalAndGlobal:
    def local(self):
        __class__ = 'local value'
        return __class__
    def global_value(self):
        global __class__
        return __class__
assert LocalAndGlobal().local() == 'local value'
assert LocalAndGlobal().global_value() == 'module value'

class Outer:
    class Inner:
        def defining(self):
            return __class__
    def defining(self):
        return __class__
assert Outer().defining() is Outer
assert Outer.Inner().defining() is Outer.Inner

def build():
    __class__ = 'outer function value'
    class Built:
        def defining(self):
            return __class__
    return Built
Built = build()
assert Built().defining() is Built

class RootMeta(type):
    def identity(cls):
        return __class__, cls
class ChildMeta(RootMeta):
    def identity(cls):
        defining, receiver = super().identity()
        assert defining is RootMeta
        assert receiver is cls
        return __class__, cls
class Subject(metaclass=ChildMeta):
    pass
assert Subject.identity() == (ChildMeta, Subject)
assert Subject.identity.__func__ is ChildMeta.identity
assert Subject.identity.__self__ is Subject
print('class lexical closure regression passed')

assert Subject.identity.__func__.__closure__ is ChildMeta.identity.__closure__
class NoCell:
    def local(self, __class__):
        return __class__
    def global_value(self):
        global __class__
        return __class__
assert NoCell.local.__closure__ is None
assert NoCell.global_value.__closure__ is None
print('class cell identity and unused-cell checks passed')
