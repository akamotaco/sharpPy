

class LexicalMeta(type):
    def __iter__(cls):
        assert __class__ is LexicalMeta
        return cls
    def __next__(cls):
        assert __class__ is LexicalMeta
        return 17
class Lexical(metaclass=LexicalMeta):
    pass
assert next(iter(Lexical)) == 17
print('metaclass lexical closure passed')
assert next(Lexical.__iter__()) == 17
