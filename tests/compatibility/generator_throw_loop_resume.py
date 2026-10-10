# Existing gap: after handling throw(), continue to the next loop yield.
def sequence():
    for i in range(3):
        try:
            yield i
        except ValueError:
            pass

g = sequence()
assert next(g) == 0
assert g.throw(ValueError('injected')) == 1
assert next(g) == 2
print('generator throw loop resume: PASS')
