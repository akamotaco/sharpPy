"""What subprocess imports on a platform without msvcrt. Only for running tests."""


def fork_exec(*args):
    raise OSError("no subprocess here")
