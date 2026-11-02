"""What Lib/importlib/machinery.py names, for the native import system."""
SOURCE_SUFFIXES = ['.py']
DEBUG_BYTECODE_SUFFIXES = ['.pyc']
OPTIMIZED_BYTECODE_SUFFIXES = ['.pyc']
BYTECODE_SUFFIXES = ['.pyc']
EXTENSION_SUFFIXES = []


def all_suffixes():
    """Returns a list of all recognized module suffixes for this process"""
    return SOURCE_SUFFIXES + BYTECODE_SUFFIXES + EXTENSION_SUFFIXES


class ModuleSpec:
    def __init__(self, name, loader, *, origin=None, loader_state=None, is_package=None):
        self.name = name
        self.loader = loader
        self.origin = origin
        self.loader_state = loader_state
        self.submodule_search_locations = [] if is_package else None


class BuiltinImporter:
    pass


class FrozenImporter:
    pass


class PathFinder:
    pass


class FileFinder:
    pass


class SourceFileLoader:
    pass


class SourcelessFileLoader:
    pass


class ExtensionFileLoader:
    pass
