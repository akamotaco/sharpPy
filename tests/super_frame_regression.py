def error(call, message):
    try:
        call()
    except RuntimeError as exc:
        assert str(exc) == message, str(exc)
    else:
        assert False, message

factory = super

def no_args():
    return factory()
error(no_args, 'super(): no arguments')

def local_only():
    value = 1
    return factory()
error(local_only, 'super(): no arguments')

def missing(value):
    return factory()
error(lambda: missing(None), 'super(): __class__ cell not found')

def deleted(value):
    del value
    return factory()
error(lambda: deleted(None), 'super(): arg[0] deleted')

def keyword_only(*, value=1):
    return factory()
error(keyword_only, 'super(): no arguments')

class Base:
    def value(self):
        return 9
class Derived(Base):
    def captured(self):
        capture = lambda: self
        make = super
        return make().value()
    def reassigned(self):
        capture = lambda: self
        self = Derived()
        make = super
        return make().value()
    def deleted(self):
        capture = lambda: self
        del self
        make = super
        return make()
assert Derived().captured() == 9
assert Derived().reassigned() == 9
error(lambda: Derived().deleted(), 'super(): arg[0] deleted')

class InvalidClass:
    def call(self):
        nonlocal __class__
        __class__ = 42
        make = super
        return make()
error(lambda: InvalidClass().call(), 'super(): __class__ is not a type (int)')

class EmptyClass:
    def call(self):
        nonlocal __class__
        del __class__
        make = super
        return make()
error(lambda: EmptyClass().call(), 'super(): empty __class__ cell')
print('super frame regression passed')
