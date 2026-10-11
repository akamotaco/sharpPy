"""Pre-existing differences found while testing explicit type descriptors."""
class Child: pass
print(type.__repr__(Child))
class Meta(type):
    @property
    def value(cls): return 'data descriptor'
class Example(metaclass=Meta): value = 'local'
print(Example.value)
print('type dict', '__setattr__' in type.__dict__)
