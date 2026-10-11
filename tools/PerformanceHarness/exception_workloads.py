def exception_construction(n):
    total = 0
    for i in range(n):
        error = ValueError("message")
        total += i
    return total


def lookup_exception_construction(n):
    total = 0
    for i in range(n):
        first = NameError("message")
        second = AttributeError("message")
        total += i
    return total


def os_error_construction(n):
    total = 0
    for i in range(n):
        first = OSError("message")
        second = FileNotFoundError("message")
        total += i
    return total


def os_error_fallback(n):
    total = 0
    for i in range(n):
        error = OSError(999, "message")
        total += i
    return total


def os_error_winerror(n):
    total = 0
    for i in range(n):
        error = OSError(999, "message", "source", 267)
        total += i
    return total


class _ErrorCode(int):
    pass


def int_subclass_construction(n):
    total = 0
    for i in range(n):
        code = _ErrorCode(i)
        total += i
    return total


def os_error_subclass_lookup(n):
    code = _ErrorCode(2)
    total = 0
    for i in range(n):
        error = OSError(code, "message")
        total += i
    return total


def integer_hash(n):
    total = 0
    for i in range(n):
        value = hash(2)
        total += i
    return total
