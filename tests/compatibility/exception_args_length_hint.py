calls = []
class Source:
    def __iter__(self):
        calls.append('iter')
        return iter([1, 2])
    def __len__(self):
        calls.append('len')
        raise RuntimeError('length failed')
error = StopIteration('old')
saved = error.args
try:
    error.args = Source()
except RuntimeError as failure:
    assert str(failure) == 'length failed'
else:
    raise AssertionError('expected length hint failure')
assert calls == ['iter', 'len']
assert error.args is saved
print('exception args length hint: PASS')
