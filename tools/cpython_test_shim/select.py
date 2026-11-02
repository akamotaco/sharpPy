"""What selectors reads from select. Only for importing subprocess in tests."""
error = OSError


def select(*args):
    raise OSError("no select here")
