"""CPython rich comparison order, binding and result propagation."""
events = []

def show(label, operation):
    events.clear()
    try:
        result = operation()
        print(label, result, events)
    except Exception as error:
        print(label, type(error).__name__, str(error), events)


class Base:
    def __eq__(self, other):
        events.append('base.eq')
        return NotImplemented

    def __lt__(self, other):
        events.append('base.lt')
        return NotImplemented


class Child(Base):
    def __eq__(self, other):
        events.append('child.eq')
        return NotImplemented

    def __gt__(self, other):
        events.append('child.gt')
        return NotImplemented


base = Base()
child = Child()
show('subclass equality', lambda: base == child)
show('subclass ordering', lambda: base < child)
show('same object', lambda: child == child)
show('same type', lambda: Base() == Base())
show('object fallback', lambda: object() == child)

class Answer(Base):
    def __eq__(self, other):
        events.append('answer.eq')
        return 'answer'

    def __gt__(self, other):
        events.append('answer.gt')
        return 'greater'

show('subclass value', lambda: base == Answer())
show('ordering value', lambda: base < Answer())

class Number(int):
    def __eq__(self, other):
        events.append('number.eq')
        return NotImplemented

    def __lt__(self, other):
        events.append('number.lt')
        return NotImplemented

show('int left', lambda: 1 == Number(1))
show('int right', lambda: Number(1) == 1)
show('two overridden ints', lambda: Number(1) == Number(1))
show('float left', lambda: 1.0 == Number(1))
show('float right', lambda: Number(1) == 1.0)
show('bool left', lambda: True == Number(1))
show('int inherited ne', lambda: Number(1) != Number(2))
show('declined ordering', lambda: Number(1) < Number(2))

class Truth:
    def __bool__(self):
        events.append('truth.bool')
        return False

marker = Truth()

class Equal:
    def __eq__(self, other):
        events.append('equal.eq')
        return marker

show('preserved result', lambda: (Equal() == Equal()) is marker)
show('object default ne', lambda: Equal() != Equal())
show('numeric reflected', lambda: (1 == Equal()) is marker)
show('float reflected', lambda: (1.0 == Equal()) is marker)
show('bool reflected', lambda: (True == Equal()) is marker)
show('unsupported subclass', lambda: Number(1) < None)
show('unsupported float', lambda: 1.0 < object())
show('unsupported bool', lambda: True < object())
show('None ordering', lambda: None <= None)
show('None reflected', lambda: (None == Equal()) is marker)
show('not comparison', lambda: not (Equal() == Equal()))
show('chained comparison', lambda: (Equal() == Equal() == 1) is marker)

def conditional():
    if Equal() == Equal():
        return 'wrong branch'
    return 'false branch'

show('conditional', conditional)

class Bound:
    def __get__(self, instance, owner):
        events.append('bind')
        def compare(other):
            events.append('call')
            return 'bound'
        return compare

class Descriptor(int):
    __eq__ = Bound()

show('descriptor', lambda: Descriptor(1) == 2)
show('descriptor again', lambda: Descriptor(1) == 2)

class BrokenBinding:
    def __get__(self, instance, owner):
        events.append('broken bind')
        raise ValueError('binding')

class Broken(int):
    __eq__ = BrokenBinding()

show('binding error cleared', lambda: Broken(1) == 1)

class BrokenCall(int):
    def __eq__(self, other):
        raise ValueError('comparison body')

show('body error', lambda: BrokenCall(1) == 1)

class Disabled(int):
    __eq__ = None

show('not callable', lambda: Disabled(1) == 1)

class Static(int):
    @staticmethod
    def __eq__(other):
        return 'static'

class ClassMethod(int):
    @classmethod
    def __eq__(cls, other):
        return cls.__name__

show('static', lambda: Static(1) == 1)
show('class method', lambda: ClassMethod(1) == 1)

class Dynamic(int):
    pass

class Descendant(Dynamic):
    pass

number = Descendant(1)
show('before edit', lambda: number == 1)
Dynamic.__eq__ = lambda self, other: 'edited'
show('after parent edit', lambda: number == 1)
del Dynamic.__eq__
show('after parent delete', lambda: number == 1)
number.__eq__ = lambda other: 'instance'
show('ignore instance', lambda: number == 1)

class Hooks(int):
    def __getattribute__(self, name):
        raise AssertionError('instance attribute hook')

    def __eq__(self, other):
        return 'type lookup'

show('ignore hook', lambda: Hooks(1) == 1)

class Identity(int):
    __eq__ = object.__eq__
    __ne__ = object.__ne__

first = Identity(1)
second = Identity(1)
show('explicit object eq', lambda: object.__eq__(first, second) is NotImplemented)
show('restored object eq', lambda: first == second)
show('restored object ne', lambda: first != second)
show('restored object identity', lambda: first == first)

class Mixin:
    def __eq__(self, other):
        return 'mixin'

class IntFirst(int, Mixin):
    pass

class MixinFirst(Mixin, int):
    pass

show('int before mixin', lambda: IntFirst(1) == 1)
show('mixin before int', lambda: MixinFirst(1) == 1)

class Mutating(int):
    def __eq__(self, other):
        Mutating.__eq__ = lambda self, other: 'new method'
        return 'old method'

changing = Mutating(1)
show('edit during call', lambda: changing == 1)
show('after call edit', lambda: changing == 1)
del Mutating.__eq__
show('after call delete', lambda: changing == 1)
setattr(Dynamic, '__eq__', lambda self, other: 'setattr')
show('setattr invalidation', lambda: number == 1)
delattr(Dynamic, '__eq__')
show('delattr invalidation', lambda: number == 1)
