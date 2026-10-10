"""close must propagate cleanup errors instead of replacing their type."""
def failing_cleanup():
    try:
        yield 1
    finally:
        raise ValueError('cleanup failure')

g = failing_cleanup()
assert next(g) == 1
try:
    g.close()
except ValueError as error:
    assert str(error) == 'cleanup failure'
else:
    assert False, 'close swallowed the cleanup exception'
print('generator close exception: PASS')
