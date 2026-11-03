"""What test.support.import_helper reads from _imp."""


def _override_frozen_modules_for_tests(override):
    pass


def _override_multi_interp_extensions_check(override):
    return 0


def acquire_lock():
    pass


def release_lock():
    pass


def lock_held():
    return False


def extension_suffixes():
    return []


def is_builtin(name):
    import sys
    return name in sys.builtin_module_names


def is_frozen(name):
    return False


check_hash_based_pycs = "default"
