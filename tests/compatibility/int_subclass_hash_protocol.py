"""Pending: generic hash/dict dispatch, separate from OSError's errnomap."""
class Code(int):
    def __hash__(self):
        print('hash-called')
        return 2
    def __eq__(self, other):
        print('eq-called', other)
        return True

code = Code(999)
print('hash', hash(code))
mapping = {code: 'value'}
print('lookup', mapping.get(2, 'missing'))
