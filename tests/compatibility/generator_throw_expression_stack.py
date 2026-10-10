# Exception unwinding must remove unfinished expression operands, not the loop iterator.
def sequence():
    for i in range(3):
        try:
            value = 100 + (yield i)
        except ValueError:
            pass

g = sequence()
assert next(g) == 0
assert g.throw(ValueError()) == 1
assert g.send(2) == 2
g.close()
print('generator throw expression stack: PASS')
