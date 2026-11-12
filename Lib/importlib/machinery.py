"""The machinery of importlib: _frozen_importlib's, CPython's PathFinder, and the C# FileFinder and SourceFileLoader."""
from ._bootstrap import ModuleSpec
from ._bootstrap import BuiltinImporter
from ._bootstrap import FrozenImporter
from ._bootstrap_external import (SOURCE_SUFFIXES, BYTECODE_SUFFIXES, EXTENSION_SUFFIXES,
                                  PathFinder, FileFinder, SourceFileLoader, NamespaceLoader)

DEBUG_BYTECODE_SUFFIXES = BYTECODE_SUFFIXES
OPTIMIZED_BYTECODE_SUFFIXES = BYTECODE_SUFFIXES


def all_suffixes():
    """Returns a list of all recognized module suffixes for this process"""
    return SOURCE_SUFFIXES + BYTECODE_SUFFIXES + EXTENSION_SUFFIXES


class SourcelessFileLoader:
    pass


class ExtensionFileLoader:
    pass
