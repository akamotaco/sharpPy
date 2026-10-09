# Regression: large steps in copied sequences and memoryview reads.
huge = 10 ** 100
assert list(b'abcdef'[::huge]) == [97]
assert list(b'abcdef'[::-huge]) == [102]
view = memoryview(b'abcdef')
child = view[::huge]
assert len(child) == 1 and child[0] == ord('a')
child.release()
view.release()
print('large slice step: PASS')
