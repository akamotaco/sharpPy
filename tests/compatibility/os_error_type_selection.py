"""Pending: additional errno subclasses and Windows winerror translation."""
for args in ((13, 'denied'), (17, 'exists'), (999, 'missing', 'file', 2)):
    error = OSError(*args)
    print(type(error).__name__, error.args, error.errno, str(error))
