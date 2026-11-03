"""SharpPy has no sockets. Modules that a test reaches through import only need these names."""
error = OSError
timeout = TimeoutError


class herror(OSError):
    pass


class gaierror(OSError):
    pass


class socket:
    pass


def getdefaulttimeout():
    return None


def __getattr__(name):
    if name.startswith("__"):
        raise AttributeError(name)
    return lambda *args, **kwargs: ""
