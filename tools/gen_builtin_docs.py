"""Generate core/BuiltinDocs.g.cs from the running CPython 3.12.

CPython keeps one internal doc string per builtin (ml_doc, tp_doc, the wrapperbase doc), in the
Argument Clinic form "name(signature)\\n--\\n\\ndoc". __doc__ and __text_signature__ are cut out of
it at run time (Objects/typeobject.c _PyType_GetDocFromInternalDoc and
_PyType_GetTextSignatureFromInternalDoc). Only the cut pieces are visible from Python, so the
internal string is put back together here: name + __text_signature__ + "\\n--\\n\\n" + __doc__.

Keys: "<module>.<function>" for module functions, "<type>" for a type, "<type>.<member>" for
what the type's own dict holds.

usage: python tools/gen_builtin_docs.py   (run with CPython 3.12.15)
"""
import builtins
import os
import sys

MARKER = "\n--\n\n"


def internal(name, obj):
    doc = getattr(obj, "__doc__", None)
    if not isinstance(doc, str):
        doc = None
    try:
        sig = obj.__text_signature__
    except Exception:
        sig = None
    if isinstance(sig, str):
        return name + sig + MARKER + (doc or "")
    return doc


def literal(text):
    out = []
    for ch in text:
        code = ord(ch)
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ch == "\n":
            out.append("\\n")
        elif ch == "\r":
            out.append("\\r")
        elif ch == "\t":
            out.append("\\t")
        elif code < 0x20 or code > 0x7e:
            if code > 0xFFFF:
                out.append("\\U%08x" % code)
            else:
                out.append("\\u%04x" % code)
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def collect():
    entries = {}
    for name, obj in vars(builtins).items():
        if name.startswith("_") and name != "__build_class__" and name != "__import__":
            continue
        if isinstance(obj, type):
            raw = internal(obj.__name__, obj)
            if raw is not None:
                entries[obj.__name__] = raw
            for member, value in vars(obj).items():
                if member == "__doc__":
                    continue
                raw = internal(member, value)
                if raw is not None:
                    entries[f"{obj.__name__}.{member}"] = raw
        elif callable(obj):
            raw = internal(name, obj)
            if raw is not None:
                entries[f"builtins.{name}"] = raw
    # The static types the types module names (method, function, code, frame, SimpleNamespace,
    # UnionType, ...): tp_doc and their dicts, keyed as PyDocs.TypeKey keys a static type.
    import types
    for obj in vars(types).values():
        if not isinstance(obj, type) or obj.__module__ == "types" and obj.__flags__ & (1 << 9):
            continue
        key = obj.__name__ if obj.__module__ == "builtins" else f"{obj.__module__}.{obj.__name__}"
        if key in entries:
            continue
        raw = internal(obj.__name__, obj)
        if raw is not None:
            entries[key] = raw
        for member, value in vars(obj).items():
            if member == "__doc__":
                continue
            raw = internal(member, value)
            if raw is not None:
                entries.setdefault(f"{key}.{member}", raw)
    # _sre's static types, which re names (Pattern, Match) or does not (the scanner).
    import re
    # and _pickle's memo proxies, which only Pickler.memo and Unpickler.memo answer.
    import _pickle, io
    memo_proxies = (type(_pickle.Pickler(io.BytesIO()).memo), type(_pickle.Unpickler(io.BytesIO()).memo))
    for obj in (re.Pattern, re.Match, type(re.compile("").scanner("")), *memo_proxies):
        key = f"{obj.__module__}.{obj.__name__}"
        raw = internal(obj.__name__, obj)
        if raw is not None:
            entries[key] = raw
        for member, value in vars(obj).items():
            if member == "__doc__" or not (callable(value) or hasattr(type(value), "__get__")):
                continue
            raw = internal(member, value)
            if raw is not None:
                entries[f"{key}.{member}"] = raw
    # Native modules: "<module>.<name>" for what they hold; their m_doc is tools/gen_module_docs.py's.
    import _bisect, _bz2, _collections, _datetime, _decimal, _elementtree, _functools, _hashlib, _heapq, _io, _lzma, _operator, _pickle, _random, _socket, _stat, _struct, _thread, _typing, _zoneinfo, array, itertools, math, nt, pyexpat, select, sys, time
    # A static type a module readies without naming it: _datetime's IsoCalendarDate, _decimal's
    # SignalDictMixin and ContextManager.
    unnamed = {_datetime: {"IsoCalendarDate": type(_datetime.date(2000, 1, 1).isocalendar())},
               _decimal: {"SignalDictMixin": type(_decimal.getcontext().flags).__mro__[1],
                          "ContextManager": type(_decimal.localcontext())}}
    for module in (_bisect, _bz2, _collections, _datetime, _decimal, _elementtree, _functools, _hashlib, _heapq, _io, _lzma, _operator, _pickle, _random, _socket, _stat, _struct, _thread, _typing, _zoneinfo, array, itertools, math, nt, pyexpat, select, sys, time):
        for name, obj in list(vars(module).items()) + list(unnamed.get(module, {}).items()):
            # A class written in Python that a C module names (_decimal's DecimalTuple, the
            # exceptions it makes with PyErr_NewException) has no C docs.
            if isinstance(obj, type) and obj.__flags__ & (1 << 9) and module is _decimal:
                continue
            if isinstance(obj, type):
                # A type of the module: "<__module__>.<name>" and its members under that.
                key = f"{obj.__module__}.{obj.__name__}"
                raw = internal(obj.__name__, obj)
                if raw is not None:
                    entries[key] = raw
                for member, value in vars(obj).items():
                    # Only what documents itself: methods and descriptors, not stored values.
                    if member == "__doc__" or not (callable(value) or hasattr(type(value), "__get__")):
                        continue
                    raw = internal(member, value)
                    if raw is not None:
                        entries[f"{key}.{member}"] = raw
                continue
            if name.startswith("__") or not callable(obj):
                continue
            raw = internal(name, obj)
            if raw is not None:
                entries[f"{module.__name__}.{name}"] = raw
    return entries


def main():
    if sys.version_info[:2] != (3, 12):
        raise SystemExit("run with CPython 3.12")
    entries = collect()
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    lines = [
        "// <auto-generated> tools/gen_builtin_docs.py from CPython " + sys.version.split()[0] + " </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace SharpPy",
        "{",
        "    internal static partial class PyDocs",
        "    {",
        "        private static readonly Dictionary<string, string> Internal = new()",
        "        {",
    ]
    for key in sorted(entries):
        lines.append(f"            [{literal(key)}] = {literal(entries[key])},")
    lines += ["        };", "    }", "}", ""]
    with open(os.path.join(root, "core", "BuiltinDocs.g.cs"), "w", encoding="utf-8", newline="\n") as stream:
        stream.write("\n".join(lines))
    print(len(entries), "entries")


main()
