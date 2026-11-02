"""Build a SharpPy bundle (.spk): start scripts plus the Python modules they reach.

The modules are found from the bytecode, so what no one imports stays out. Whatever is not found
on --path is left to the DLL or to the import that guards it; those names are listed for review. An
import whose name is only known at run time cannot be seen: name it with --include.

usage: make_bundle.py --output game.spk --path Lib [--path more] [--include name]
                      [--exclude name] [--top-level] script.py ...

Runs under CPython 3.12 or SharpPy, whose bytecode is the same.
"""
import argparse
import dis
import hashlib
import marshal
import os
import struct
import sys
import types

MARK = b"SPKB"
FORMAT_VERSION = 1
PYTHON_MAGIC = 3531

# What a game has no use for, reached only through imports inside rarely used functions.
DEFAULT_EXCLUDES = ("subprocess", "multiprocessing", "socket", "ssl", "http", "urllib", "email",
                    "tkinter", "unittest", "doctest", "pydoc", "pdb", "test", "ctypes",
                    "concurrent.futures.process")


# encodings imports its codecs by name at run time, so nothing in the bytecode shows them.
ENCODINGS = ("aliases", "ascii", "charmap", "latin_1", "raw_unicode_escape", "unicode_escape",
             "utf_16", "utf_32", "utf_8", "utf_8_sig")


def imports_of(code, package, top_level):
    """(name, fromlist) of every IMPORT_NAME, with relative names made absolute."""
    found = []
    instructions = list(dis.get_instructions(code))
    for position, instruction in enumerate(instructions):
        if instruction.opname != "IMPORT_NAME":
            continue
        fromlist = instructions[position - 1].argval or ()
        level = instructions[position - 2].argval or 0
        name = instruction.argval
        if level:
            parts = package.split(".")
            base = ".".join(parts[:len(parts) - level + 1])
            name = f"{base}.{name}" if name else base
        found.append((name, fromlist))
    if top_level and code.co_flags & 0x2:  # CO_NEWLOCALS: a function body runs later, if ever
        return found
    for constant in code.co_consts:
        if isinstance(constant, types.CodeType):
            if top_level and constant.co_flags & 0x1:  # CO_OPTIMIZED: a function
                continue
            found.extend(imports_of(constant, package, top_level))
    return found


def locate(name, paths):
    relative = name.replace(".", os.sep)
    for path in paths:
        package = os.path.join(path, relative, "__init__.py")
        if os.path.isfile(package):
            return package, True
        module = os.path.join(path, relative + ".py")
        if os.path.isfile(module):
            return module, False
    return None, False


def excluded(name, excludes):
    return any(name == item or name.startswith(item + ".") for item in excludes)


def encoding_modules(paths, every):
    if not every:
        return [f"encodings.{name}" for name in ENCODINGS]
    found = []
    for path in paths:
        folder = os.path.join(path, "encodings")
        if os.path.isdir(folder):
            found.extend(f"encodings.{name[:-3]}" for name in os.listdir(folder)
                         if name.endswith(".py") and name != "__init__.py")
    return found


def collect(scripts, paths, includes, excludes, top_level, every_encoding):
    """name -> (file, is_package, code), and the names with no source."""
    modules = {}
    native = set()
    pending = list(includes)

    def visit(code, package):
        for name, fromlist in imports_of(code, package, top_level):
            parts = name.split(".")
            pending.extend(".".join(parts[:i + 1]) for i in range(len(parts)))
            pending.extend(f"{name}.{item}" for item in fromlist if item != "*")

    for script in scripts:
        with open(script, encoding="utf-8") as stream:
            name = os.path.splitext(os.path.basename(script))[0]
            code = compile(stream.read(), os.path.basename(script), "exec")
        modules[name] = (script, False, code)
        visit(code, "")
    while pending:
        name = pending.pop()
        if name in modules or name in native or excluded(name, excludes):
            continue
        path, is_package = locate(name, paths)
        if path is None:
            # `from module import attribute` asks for no module at all.
            if "." not in name:
                native.add(name)
            continue
        with open(path, encoding="utf-8") as stream:
            source = stream.read()
        filename = name.replace(".", "/") + ("/__init__.py" if is_package else ".py")
        code = compile(source, filename, "exec", dont_inherit=True)
        modules[name] = (path, is_package, code)
        visit(code, name if is_package else name.rpartition(".")[0])
        if name == "encodings":
            pending.extend(encoding_modules(paths, every_encoding))
    return modules, native


def write(output, modules):
    index = {}
    blobs = []
    offset = 0
    for name in sorted(modules):
        path, is_package, code = modules[name]
        blob = marshal.dumps(code)
        with open(path, "rb") as stream:
            digest = hashlib.sha256(stream.read()).digest()[:16]
        index[name] = (offset, len(blob), is_package, digest)
        blobs.append(blob)
        offset += len(blob)
    head = marshal.dumps(index)
    with open(output, "wb") as stream:
        stream.write(MARK + struct.pack("<IIII", FORMAT_VERSION, PYTHON_MAGIC, 0, len(head)))
        stream.write(head)
        for blob in blobs:
            stream.write(blob)
    return HEADER_SIZE + len(head) + offset


HEADER_SIZE = 20


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("scripts", nargs="*")
    parser.add_argument("--output", required=True)
    parser.add_argument("--path", action="append", default=[])
    parser.add_argument("--include", action="append", default=[])
    parser.add_argument("--exclude", action="append", default=[])
    parser.add_argument("--all-encodings", action="store_true",
                        help="every codec of encodings, not only the common ones")
    parser.add_argument("--top-level", action="store_true",
                        help="follow module-level imports only, not those inside functions")
    options = parser.parse_args()
    modules, native = collect(options.scripts, options.path, options.include,
                              (*DEFAULT_EXCLUDES, *options.exclude), options.top_level, options.all_encodings)
    size = write(options.output, modules)
    print(f"{options.output}: {len(modules)} modules, {size} bytes")
    print("no source found (C# modules, or optional imports):", " ".join(sorted(native)))


main()
