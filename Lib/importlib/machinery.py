"""The implementations of the import machinery: _frozen_importlib's, and the C# path finder and loaders."""
from ._bootstrap import ModuleSpec
from ._bootstrap import BuiltinImporter
from ._bootstrap import FrozenImporter
from ._bootstrap_external import (SOURCE_SUFFIXES, BYTECODE_SUFFIXES, EXTENSION_SUFFIXES,
                                  PathFinder, SourceFileLoader, NamespaceLoader)

DEBUG_BYTECODE_SUFFIXES = BYTECODE_SUFFIXES
OPTIMIZED_BYTECODE_SUFFIXES = BYTECODE_SUFFIXES


def all_suffixes():
    """Returns a list of all recognized module suffixes for this process"""
    return SOURCE_SUFFIXES + BYTECODE_SUFFIXES + EXTENSION_SUFFIXES


class FileFinder:
    pass


class SourcelessFileLoader:
    pass


class ExtensionFileLoader:
    pass
