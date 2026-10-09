"""Known gap: class body exception handling must execute its handler."""
class Example:
    try:
        raise ValueError('check')
    except ValueError:
        caught = True
assert Example.caught
print('class body exceptions: PASS')
