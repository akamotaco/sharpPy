class Meta(type):
    def __iter__(cls):
        return 42
class Subject(metaclass=Meta):
    pass
try:
    iter(Subject)
except TypeError as exc:
    assert str(exc) == "iter() returned non-iterator of type 'int'"
else:
    assert False, 'invalid metaclass iterator accepted'
print('metaclass iterator validation passed')
