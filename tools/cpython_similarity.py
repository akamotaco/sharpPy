#!/usr/bin/env python3
"""Compare SharpPy's C# implementation against the pinned CPython 3.12.15 C source.

The question this answers is structural, not behavioural: for a given mechanism, does
SharpPy have ONE implementation that sits where CPython's equivalent sits, or has it grown
an adapter, a duplicate, or a special case? Behaviour is measured elsewhere (run_bughunt.py,
evaluate.py); this tool measures shape.

Metrics
  M1 anchors      - mechanism table (ANCHORS below): each row names a CPython file:line and a
                    SharpPy file. The C line is re-read from the pinned tree and checked to
                    still contain the named function/symbol. Verdict per row: ok / moved /
                    missing / not-cited.
  M2 representations - how many distinct CLR shapes represent one Python concept (a subclass
                    instance of a builtin base), plus per-site CLR self-checks and storage API
                    references. CPython has one object and a flag, so anything above one shape
                    is bookkeeping SharpPy pays and CPython does not.
  M3 surface      - for selected builtin types, parse CPython's <type>_methods[] table and the
                    tp_* slots that are set, then check SharpPy registers a counterpart.
  M4 detours      - reflection (GetMethod/Invoke), hand-maintained capability tables, TODO
                    markers, and hard casts that have to be patched per type.

Usage:
  python tools/cpython_similarity.py [--root DIR] [--cpython DIR] [--json OUT] [--compare BEFORE.json]
Exit code is the number of broken anchors (M1), so it can gate a commit.
"""

import argparse
import io
import json
import os
import re
import subprocess
import sys

# --- M1: mechanism anchors -------------------------------------------------------------
# (mechanism, cpython file, line, symbol expected on that line, sharppy file(s))
# Lines are from tag v3.12.15 (commit e848e4b09ca). If CPython moves a line, the row reports
# "moved" and the fix is to re-find the symbol, not to delete the row.
ANCHORS = [
    ("slot inheritance (a subtype owns the base's C functions)",
     "Objects/typeobject.c", 6803, "inherit_slots", ["core/PyType.cs"]),
    ("flags/feature layer (Py_TPFLAGS_*)",
     "Include/object.h", 546, "Py_TPFLAGS_LIST_SUBCLASS", ["core/PyTypeFlags.cs"]),
    ("flag propagation to a subtype",
     "Objects/typeobject.c", 6726, "inherit_special", ["core/PyType.cs"]),
    ("type feature test used by PyList_Check & friends",
     "Include/object.h", 959, "PyType_HasFeature", ["core/PyType.cs"]),
    ("list repr (inherited by list subclasses)",
     "Objects/listobject.c", 379, "list_repr", ["type/PyList.cs"]),
    ("dict repr",
     "Objects/dictobject.c", 2400, "dict_repr", ["type/PyDict.cs"]),
    ("set repr (formats with Py_TYPE(so)->tp_name)",
     "Objects/setobject.c", 507, "set_repr", ["type/PySet.cs"]),
    ("set tp_new ignores the iterable; tp_init consumes it",
     "Objects/setobject.c", 1038, "set_new", ["core/PyType.cs"]),
    ("set_init: two different names in its two messages",
     "Objects/setobject.c", 1977, "set_init", ["core/PyType.cs"]),
    ("frozenset builds its value in tp_new (no tp_init)",
     "Objects/setobject.c", 1003, "frozenset_new", ["core/PyType.cs"]),
    ("object_new refuses arguments",
     "Objects/typeobject.c", 5444, "object_new", ["core/PyType.cs"]),
    ("repr recursion guard (Py_ReprEnter)",
     "Objects/object.c", 0, "Py_ReprEnter", ["core/PyReprGuard.cs"]),
    ("PyObject_IsTrue: nb_bool then sq_length/mp_length",
     "Objects/object.c", 1675, "PyObject_IsTrue", ["type/PyClass.cs"]),
    ("Py_DECREF reaching zero routes to tp_dealloc",
     "Objects/object.c", 2623, "_Py_Dealloc", ["core/PyFinalizer.cs"]),
    ("heap type finalizer call on dealloc",
     "Objects/typeobject.c", 1903, "PyObject_CallFinalizerFromDealloc", ["core/PyFinalizer.cs"]),
    ("io base finalize: close() on collection",
     "Modules/_io/iobase.c", 298, "iobase_finalize", ["core/PyResourceBus.cs"]),
    ("config argv -> sys.argv (COPY_LIST)",
     "Python/sysmodule.c", 3612, "COPY_LIST", ["modules/SysModule.cs", "core/PyArgv.cs"]),
]

# --- M3: which builtin types to compare method tables for -------------------------------
# The C# file that registers a builtin's descriptors. Attribution by file is a proxy for "this
# type has this slot", and it is a deliberate one: SharpPy registers each builtin's surface in the
# same file its value type lives in, the way CPython keeps each type's spec in its own .c file.
SURFACE_TYPES = [
    ("list", "Objects/listobject.c", None, "PyList_Type", "type/PyList.cs"),
    ("dict", "Objects/dictobject.c", None, "PyDict_Type", "type/PyDict.cs"),
    ("set", "Objects/setobject.c", None, "PySet_Type", "type/PySet.cs"),
    ("frozenset", "Objects/setobject.c", None, "PyFrozenSet_Type", "type/PySet.cs"),
    ("tuple", "Objects/tupleobject.c", None, "PyTuple_Type", "type/PyTuple.cs"),
    ("str", "Objects/unicodeobject.c", None, "PyUnicode_Type", "type/PyString.cs"),
]

CODE_DIRS = ["core", "type", "runtime", "modules", "parser"]


def read(path):
    try:
        with io.open(path, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def cs_files(root):
    out = []
    for d in CODE_DIRS:
        base = os.path.join(root, d)
        for dirpath, _dirs, files in os.walk(base):
            for name in files:
                if name.endswith(".cs"):
                    out.append(os.path.join(dirpath, name))
    return sorted(out)


# ----------------------------------------------------------------------------------------
def metric_anchors(root, cpython):
    rows = []
    for mech, cfile, line, symbol, csfiles in ANCHORS:
        src = read(os.path.join(cpython, cfile))
        lines = src.split("\n")
        status, found = "missing", None
        if line and line <= len(lines):
            if symbol in lines[line - 1]:
                status = "ok"
                found = lines[line - 1].strip()[:70]
        if status == "missing":
            for i, l in enumerate(lines, 1):
                if symbol and symbol in l:
                    status, found = "moved", "%s:%d" % (cfile, i)
                    break
        cited = []
        for cs in csfiles:
            text = read(os.path.join(root, cs))
            if not text:
                cited.append("%s:ABSENT" % cs)
            elif ("%s:" % cfile) in text or symbol in text:
                cited.append("%s:cited" % cs)
            else:
                cited.append("%s:no-citation" % cs)
        rows.append({"mechanism": mech, "cpython": "%s:%s" % (cfile, line or "?"),
                     "symbol": symbol, "c_source": status, "found_at": found,
                     "sharppy": cited})
    broken = sum(1 for r in rows if r["c_source"] != "ok")
    uncited = sum(1 for r in rows if any("no-citation" in x or "ABSENT" in x for x in r["sharppy"]))
    return {"rows": rows, "total": len(rows), "broken_c_anchors": broken,
            "rows_without_sharppy_citation": uncited}


def metric_representations(root):
    wrapper_classes = []
    storage_fields = []
    self_checks = 0
    storage_refs = 0
    reflection = 0
    for path in cs_files(root):
        text = read(path)
        rel = os.path.relpath(path, root).replace("\\", "/")
        for m in re.finditer(r"class\s+(Py\w+)\s*:\s*(Py(?:List|Dict|Set|FrozenSet|Tuple|Bytes|String|Int|Float))\b", text):
            wrapper_classes.append("%s %s:%s" % (m.group(1), rel, text[:m.start()].count("\n") + 1))
        for m in re.finditer(r"private\s+(?:readonly\s+)?(?:List<PyObject>|PyDict|HashSet<PyObject>|string)\s+(_\w*[Ss]torage)", text):
            storage_fields.append("%s %s:%d" % (m.group(1), rel, text[:m.start()].count("\n") + 1))
        self_checks += len(re.findall(r"\bis not? (?:PyList|PyDict|PySet|PyFrozenSet)\b", text))
        self_checks += len(re.findall(r"\((?:PyList|PyDict|PySet|PyFrozenSet)\)\s*self\b", text))
        storage_refs += len(re.findall(r"GetListStorage|GetDictStorage|GetSetStorage|GetFrozenStorage|NativeStorage|IsListSubclass|IsDictSubclass|IsSetSubclass|IsFrozenSetSubclass", text))
        reflection += len(re.findall(r"\.GetMethod\(|\.Invoke\(", text))
    return {"subclass_wrapper_classes": sorted(wrapper_classes),
            "wrapper_class_count": len(wrapper_classes),
            "value_storage_fields": sorted(storage_fields),
            "value_storage_field_count": len(storage_fields),
            "builtin_type_self_checks": self_checks,
            "storage_api_references": storage_refs,
            "reflection_calls": reflection}


def cpython_method_table(cpython, cfile, table):
    src = read(os.path.join(cpython, cfile))
    m = re.search(r"static PyMethodDef %s\[\]\s*=\s*\{(.*?)\n\};" % re.escape(table), src, re.S)
    if not m:
        return []
    return sorted(set(re.findall(r'\{\s*"(\w+)"', m.group(1))))


# CPython's own table name for the methods of each type is written into the type spec as
# .tp_methods = <name>, so the spec is read instead of guessing the identifier.
def spec_block(cpython, cfile, spec):
    src = read(os.path.join(cpython, cfile))
    m = re.search(r"PyTypeObject %s = \{(.*?)\n\};" % re.escape(spec), src, re.S)
    return m.group(1) if m else ""


# Which Python-level dunder a slot corresponds to. Slots with no Python name (tp_dealloc,
# tp_traverse, tp_new, tp_flags, tp_name, ...) are counted separately as "internal": CPython
# fills them in C and a Python-visible comparison would be meaningless.
SLOT_TO_DUNDER = {
    "repr": "__repr__", "str": "__str__", "hash": "__hash__", "iter": "__iter__",
    "iternext": "__next__", "call": "__call__", "richcompare": "__eq__",
    "bool": "__bool__", "len": "__len__", "getattr": "__getattr__",
    "getattro": "__getattribute__", "setattro": "__setattr__", "delattr": "__delattr__",
    "contains": "__contains__", "init": "__init__", "new": None, "descr_get": "__get__",
    "descr_set": "__set__", "finalize": "__del__", "getbuffer": "__getbuffer__",
    "new": "__new__", "init": "__init__", "hash": "__hash__",
}
SLOT_PARTS = {
    "sq_": {"length": "__len__", "item": "__getitem__", "ass_item": "__setitem__",
            "ass_slice": "__setitem__", "slice": "__getitem__", "concat": "__add__",
            "inplace_concat": "__iadd__", "repeat": "__mul__",
            "inplace_repeat": "__imul__", "contains": "__contains__"},
    "mp_": {"length": "__len__", "subscript": "__getitem__",
            "ass_subscript": "__setitem__"},
    "nb_": {"add": "__add__", "subtract": "__sub__", "multiply": "__mul__",
            "floor_divide": "__floordiv__", "true_divide": "__truediv__",
            "remainder": "__mod__", "divmod": "__divmod__", "power": "__pow__",
            "negative": "__neg__", "positive": "__pos__", "absolute": "__abs__",
            "invert": "__invert__", "lshift": "__lshift__", "rshift": "__rshift__",
            "and": "__and__", "xor": "__xor__", "or": "__or__",
            "inplace_add": "__iadd__", "inplace_subtract": "__isub__",
            "inplace_multiply": "__imul__", "inplace_and": "__iand__",
            "inplace_or": "__ior__", "inplace_xor": "__ixor__",
            "inplace_lshift": "__ilshift__", "inplace_rshift": "__irshift__",
            "inplace_remainder": "__imod__", "inplace_floor_divide": "__ifloordiv__",
            "inplace_true_divide": "__itruediv__", "inplace_power": "__ipow__",
            "index": "__index__", "bool": "__bool__", "float": "__float__",
            "int": "__int__", "abs": "__abs__", "neg": "__neg__", "invert": "__invert__"},
}


# CPython 3.12 fills a static spec positionally and names each field in a trailing comment
# ("    list_repr,  /* tp_repr */"), so the comment is the parser, not ".field = value".
FIELD_COMMENT = re.compile(r"/\*\s*((?:tp|sq|mp|nb|am|bf|as|ss|pf)_\w+)\s*\*/")


def filled_slots(block):
    """Slot name -> value for every field in `block` whose comment is present and whose value is
    not 0/NULL. Covers a type spec and its sub-structs in one pass."""
    out = {}
    for line in block.split("\n"):
        if line.strip().startswith("/*") and line.rstrip().endswith("*/") and "*/" in line:
            continue
        m = FIELD_COMMENT.search(line)
        if not m:
            continue
        value = line[:m.start()].strip().rstrip(",").strip()
        if not value or value in ("0", "NULL", "Py_RETURN_NONE"):
            continue
        out[m.group(1)] = value
    return out


def cpython_spec_slots(cpython, cfile, spec):
    """The Python-visible dunders a type spec actually implements, plus how many C-only slots it
    fills. A slot that is 0 in the spec is not implemented and inherits; counting it as missing
    would blame SharpPy for something CPython does not do either."""
    src = read(os.path.join(cpython, cfile))
    block = spec_block(cpython, cfile, spec)
    if not block:
        return set(), set()
    fields = filled_slots(block)
    visible, internal = set(), set()
    for name, value in fields.items():
        if name == "tp_methods":
            continue
        if name.startswith("tp_"):
            key = name[3:]
            d = SLOT_TO_DUNDER.get(key)
            if d:
                visible.add(d)
            else:
                internal.add(name)
            continue
        # sub-struct member: the spec names the struct, the struct's own comment names the slot
        table = SLOT_PARTS.get(name[:3])
        if table is None:
            internal.add(name)
            continue
        key = name[3:]
        if key in table:
            visible.add(table[key])
        else:
            internal.add(name)
    # members of the sub-struct definitions in the same file
    for struct in re.findall(r"Py(?:Sequence|Mapping|Number|AsyncMethods|BufferProcs)\s+(\w+)\s*=\s*\{", src):
        members = re.search(r"Py(?:Sequence|Mapping|Number|AsyncMethods|BufferProcs)\s+%s\s*=\s*\{(.*?)\n\};" %
                            re.escape(struct), src, re.S)
        if not members:
            continue
        for name, value in filled_slots(members.group(1)).items():
            table = SLOT_PARTS.get(name[:3])
            if table and name[3:] in table:
                visible.add(table[name[3:]])
            else:
                internal.add(name)
    return visible, internal, fields


def registrations(path):
    """Names a C# file registers on a builtin type: TypeDict entries, descriptors and the
    CLR-method shape that backs a protocol operation (ToRepr, Length, Contains, Add, ...)."""
    text = read(path)
    names = set(re.findall(r'TypeDict\[\s*"(\w+)"\s*\]', text))
    names |= set(re.findall(r'new PyMethodDescriptor\(\s*Py[\w.]*\s*,\s*"(\w+)"', text))
    names |= set(re.findall(r'new PyBuiltinMethod\(\s*"(\w+)"', text))
    return names


def metric_surface(root, cpython):
    rows = []
    for name, cfile, table, spec, csfile in SURFACE_TYPES:
        vis, internal, fields = cpython_spec_slots(cpython, cfile, spec)
        table = fields.get("tp_methods", "").strip("&") or table
        c_methods = cpython_method_table(cpython, cfile, table) if table else []
        # SharpPy registers part of each builtin's surface in core/PyType.cs instead of the type's
        # own file, so coverage counts both, and the split is reported: CPython keeps a type's spec
        # in that type's .c file, so "registered elsewhere" is a shape difference, not a judgement.
        here = registrations(os.path.join(root, csfile))
        central = registrations(os.path.join(root, "core/PyType.cs"))
        both = here | central
        have_m = [m for m in c_methods if m in both]
        have_s = sorted(d for d in vis if d in both)
        rows.append({"type": name, "methods_table": table or "-", "csfile": csfile,
                     "registered_in_own_file": len(here), "registered_in_central_table": len(central),
                     "cpython_methods": len(c_methods), "methods_registered": len(have_m),
                     "methods_missing": [m for m in c_methods if m not in both],
                     "cpython_slots_visible": len(vis), "slots_registered": len(have_s),
                     "slots_missing": sorted(vis - both),
                     "slots_internal": len(internal)})
    return {"rows": rows}


def metric_detours(root):
    todo = 0
    capability_tables = 0
    per_type_switch = 0
    for path in cs_files(root):
        text = read(path)
        todo += len(re.findall(r"//\s*(TODO|FIXME|미구현|나중에)", text))
        if "_nativeCaps" in text or "NativeBuiltinState" in text:
            capability_tables += 1
        per_type_switch += len(re.findall(r'GetTypeName\(\)\s*==\s*"', text))
    return {"todo_markers": todo, "hand_maintained_capability_sites": capability_tables,
            "type_name_string_comparisons": per_type_switch}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--cpython", default=os.path.join(
        os.path.expanduser("~"), "Desktop", "work", "cpython-v3.12.15"))
    ap.add_argument("--json", default=None)
    ap.add_argument("--compare", default=None)
    a = ap.parse_args()

    out = {
        "M1_anchors": metric_anchors(a.root, a.cpython),
        "M2_representations": metric_representations(a.root),
        "M3_surface": metric_surface(a.root, a.cpython),
        "M4_detours": metric_detours(a.root),
    }
    print("=== M1 mechanism anchors ===")
    for r in out["M1_anchors"]["rows"]:
        flag = "OK  " if r["c_source"] == "ok" else ("MOVE" if r["c_source"] == "moved" else "MISS")
        print("  %s %-58s %-34s %s" % (flag, r["mechanism"][:58], r["cpython"],
                                       ",".join(r["sharppy"])[:44]))
    print("  totals: %d rows, %d broken C anchors, %d without a SharpPy citation"
          % (out["M1_anchors"]["total"], out["M1_anchors"]["broken_c_anchors"],
             out["M1_anchors"]["rows_without_sharppy_citation"]))

    m2 = out["M2_representations"]
    print("=== M2 representations of one concept (CPython: 1 object + a flag) ===")
    print("  wrapper classes: %d, value storage fields: %d" %
          (m2["wrapper_class_count"], m2["value_storage_field_count"]))
    print("  builtin self-checks: %d, storage API refs: %d, reflection: %d" %
          (m2["builtin_type_self_checks"], m2["storage_api_references"], m2["reflection_calls"]))
    print("=== M3 surface vs the CPython type spec (methods table + filled slots) ===")
    for r in out["M3_surface"]["rows"]:
        print("  %-9s methods %3d/%3d (%s)  slots %3d/%3d  own-file %3d central %3d  missing %s" %
              (r["type"], r["methods_registered"], r["cpython_methods"], r["methods_table"],
               r["slots_registered"], r["cpython_slots_visible"],
               r["registered_in_own_file"], r["registered_in_central_table"],
               ",".join((r["methods_missing"] + r["slots_missing"])[:5]) or "-"))
    m4 = out["M4_detours"]
    print("=== M4 detour markers ===")
    print("  TODO/FIXME: %d, capability tables: %d, type-name string compares: %d" %
          (m4["todo_markers"], m4["hand_maintained_capability_sites"],
           m4["type_name_string_comparisons"]))

    if a.compare:
        before = json.load(io.open(a.compare, encoding="utf-8"))
        print("=== delta vs %s ===" % os.path.basename(a.compare))
        for key, label in (("builtin_type_self_checks", "builtin self-checks"),
                           ("storage_api_references", "storage API refs"),
                           ("reflection_calls", "reflection calls")):
            b = before["M2_representations"][key]
            n = m2[key]
            print("  %-22s %5d -> %5d  (%+d, %.1f%%)" %
                  (label, b, n, n - b, 100.0 * (n - b) / max(1, b)))
        for key, label in (("todo_markers", "TODO markers"),
                           ("type_name_string_comparisons", "type-name compares")):
            b = before["M4_detours"][key]
            n = m4[key]
            print("  %-22s %5d -> %5d  (%+d)" % (label, b, n, n - b))
        for r_new, r_old in zip(out["M3_surface"]["rows"], before["M3_surface"]["rows"]):
            print("  %-9s methods %d->%d  slots %d->%d (of %d/%d)" %
                  (r_new["type"], r_old["methods_registered"], r_new["methods_registered"],
                   r_old["slots_registered"], r_new["slots_registered"],
                   r_new["cpython_methods"], r_new["cpython_slots_visible"]))

    if a.json:
        json.dump(out, io.open(a.json, "w", encoding="utf-8"), indent=1)
        print("wrote %s" % a.json)
    return out["M1_anchors"]["broken_c_anchors"]


if __name__ == "__main__":
    sys.exit(main())
