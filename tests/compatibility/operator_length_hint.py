import _operator
class Source:
    def __length_hint__(self):
        return 3
assert _operator.length_hint(Source()) == 3
print('operator length hint: PASS')
