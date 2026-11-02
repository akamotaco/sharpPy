"""Modules/_tracemalloc.c: SharpPy does not trace allocations, so tracing never starts."""


def is_tracing():
    return False


def clear_traces():
    pass


def start(nframe=1, /):
    raise RuntimeError("tracemalloc is not available")


def stop():
    pass


def get_traceback_limit():
    return 1


def get_traced_memory():
    return (0, 0)


def reset_peak():
    pass


def get_tracemalloc_memory():
    return 0


def _get_object_traceback(obj):
    return None


def _get_traces():
    return []
