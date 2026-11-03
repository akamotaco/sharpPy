"""What subprocess imports on Windows. Only for importing subprocess in tests: every name is a
callable that refuses, which also stands in for the constants subprocess reads at import."""


def _refuse(*args, **kwargs):
    raise OSError("no subprocess here")


def __getattr__(name):
    if name.startswith("__"):
        raise AttributeError(name)
    return _refuse
