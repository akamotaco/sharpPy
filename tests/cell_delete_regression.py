"""Deleting a captured local must empty the cell used by its closure."""

def captured(x):
    def read():
        return x
    assert read() == 1
    del x
    try:
        read()
    except NameError:
        pass
    else:
        raise AssertionError('deleted cell retained its value')
captured(1)
print('cell delete regression: PASS')
