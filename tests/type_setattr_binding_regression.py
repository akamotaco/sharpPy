class Parent(int): pass
class Child(Parent): pass
x = Child(1)
print('before', x == 1, x < 2)
type.__setattr__(Parent, '__eq__', lambda self, other: 'parent eq')
type.__setattr__(Parent, '__lt__', lambda self, other: 'parent lt')
print('after', x == 1, x < 2)
type.__setattr__(Child, '__eq__', lambda self, other: 'child eq')
print('child', x == 1)
type.__setattr__(Child, 'plain', 42)
print('plain', Child.plain, x.plain)
print('repr', type.__repr__(int))

class Meta(type): pass
class Example(metaclass=Meta): value = 'local'
print('local', Example.value)
type.__setattr__(Example, 'other', 'assigned')
print('metaclass', Example.other)
print('type dict', '__name__' in type.__dict__)
Created = type.__new__(type, 'Created', (), {})
type.__setattr__(Created, 'answer', 42)
print('created', Created.answer)
