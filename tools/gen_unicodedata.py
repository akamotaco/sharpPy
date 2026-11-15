"""core/UnicodedataDb.g.cs: CPython 3.12's Modules/unicodedata_db.h and Modules/unicodename_db.h.

The tables Modules/unicodedata.c reads are copied as they are: the database records and their
two-level index, the names of the categories, bidirectional classes and east asian widths, the
decomposition prefixes, data and index, the composition index and data with nfc_first/nfc_last,
the change records of 3.2.0 with their index and normalization_3_2_0, and the name tables
(lexicon, phrasebook, code_hash, name_aliases, named_sequences). Py_UNICODE_TONUMERIC is
_PyUnicode_ToNumeric, the generated switch of Objects/unicodetype_db.h, kept as (code, value).
"""
import base64
import os
import re
import struct
import textwrap
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CPYTHON = os.path.join(ROOT, "..", "cpython-v3.12.15")
DATA = open(os.path.join(CPYTHON, "Modules", "unicodedata_db.h"), encoding="utf-8").read()
NAMES = open(os.path.join(CPYTHON, "Modules", "unicodename_db.h"), encoding="utf-8").read()
TYPES = open(os.path.join(CPYTHON, "Objects", "unicodetype_db.h"), encoding="utf-8").read()
OUTPUT = os.path.join(ROOT, "core", "UnicodedataDb.g.cs")


def body(source, name):
    match = re.search(r"\b" + re.escape(name) + r"\[\] = \{(.*?)\n\};", source, re.S)
    return match.group(1)


def numbers(text):
    return [int(v, 0) for v in re.findall(r"0x[0-9A-Fa-f]+|\d+", text)]


def strings(source, name):
    return re.findall(r'"((?:[^"\\]|\\.)*)"', body(source, name))


def define(source, name):
    return int(re.search(r"#define " + name + r" (\d+)", source).group(1))


def constant(source, name):
    return int(re.search(r"static const unsigned int " + name + r" = (0x[0-9A-Fa-f]+|\d+);", source).group(1), 0)


def pack(values, width):
    fmt = {1: "B", 2: "H", 4: "I"}[width]
    raw = struct.pack("<%d%s" % (len(values), fmt), *values)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    return base64.b64encode(packer.compress(raw) + packer.flush()).decode("ascii")


def literal_string(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    out = []

    def blob(name, values, width):
        chunks = textwrap.wrap(pack(values, width), 110)
        out.append(f"        // {len(values)} values of {width} byte(s)\n")
        out.append(f"        internal const string {name} =\n" + " +\n".join(f'            "{c}"' for c in chunks) + ";\n")

    def ints(name, values):
        out.append(f"        internal static readonly int[] {name} = {{ " + ", ".join(str(v) for v in values) + " };\n")

    def names(name, values):
        out.append(f"        internal static readonly string[] {name} = {{ " + ", ".join(literal_string(v) for v in values) + " };\n")

    version = re.search(r'#define UNIDATA_VERSION "([^"]+)"', DATA).group(1)
    out.append(f'        internal const string UnidataVersion = "{version}";\n')
    # _PyUnicode_Database_Records: category, combining, bidirectional, mirrored, east_asian_width,
    # normalization_quick_check
    records = numbers(body(DATA, "_PyUnicode_Database_Records"))
    assert len(records) % 6 == 0
    blob("Records", records, 1)
    names("CategoryNames", [v for v in strings(DATA, "_PyUnicode_CategoryNames")])
    names("BidirectionalNames", strings(DATA, "_PyUnicode_BidirectionalNames"))
    names("EastAsianWidthNames", strings(DATA, "_PyUnicode_EastAsianWidthNames"))
    names("DecompPrefix", strings(DATA, "decomp_prefix"))
    out.append(f"        internal const int Shift = {define(DATA, 'SHIFT')};\n")
    blob("Index1", numbers(body(DATA, "index1")), 2)
    blob("Index2", numbers(body(DATA, "index2")), 2)
    blob("DecompData", numbers(body(DATA, "decomp_data")), 4)
    out.append(f"        internal const int DecompShift = {define(DATA, 'DECOMP_SHIFT')};\n")
    blob("DecompIndex1", numbers(body(DATA, "decomp_index1")), 1)
    blob("DecompIndex2", numbers(body(DATA, "decomp_index2")), 2)
    out.append(f"        internal const int CompShift = {define(DATA, 'COMP_SHIFT')};\n")
    out.append(f"        internal const int TotalFirst = {define(DATA, 'TOTAL_FIRST')};\n")
    out.append(f"        internal const int TotalLast = {define(DATA, 'TOTAL_LAST')};\n")
    blob("CompIndex", numbers(body(DATA, "comp_index")), 2)
    blob("CompData", numbers(body(DATA, "comp_data")), 4)
    # struct reindex {int start; short count, index;}, three ints a row, ending at start 0
    ints("NfcFirst", numbers(body(DATA, "nfc_first")))
    ints("NfcLast", numbers(body(DATA, "nfc_last")))
    # change_record: bidir, category, decimal, mirrored, east_asian_width (bytes), numeric (double)
    changes = re.findall(r"\{([^{}]*)\}", body(DATA, "change_records_3_2_0"))
    rows = [[v.strip() for v in row.split(",")] for row in changes]
    assert all(len(row) == 6 for row in rows)
    ints("ChangeRecordBytes", [int(v) for row in rows for v in row[:5]])
    out.append("        internal static readonly double[] ChangeRecordNumeric = { " + ", ".join(repr(float(row[5])) for row in rows) + " };\n")
    blob("Changes320Index", numbers(body(DATA, "changes_3_2_0_index")), 1)
    blob("Changes320Data", numbers(body(DATA, "changes_3_2_0_data")), 1)
    start = DATA.index("static Py_UCS4 normalization_3_2_0(Py_UCS4 n)")
    pairs = re.findall(r"case (0x[0-9A-Fa-f]+): return (0x[0-9A-Fa-f]+);", DATA[start:DATA.index("default:", start)])
    ints("Normalization320", [int(v, 0) for pair in pairs for v in pair])

    # _PyUnicode_ToNumeric: each run of case labels shares one return
    start = TYPES.index("double _PyUnicode_ToNumeric(Py_UCS4 ch)")
    switch = TYPES[start:TYPES.index("return -1.0;", start)]
    codes, values = [], []
    pending = []
    for line in switch.splitlines():
        line = line.strip()
        m = re.match(r"case (0x[0-9A-Fa-f]+):", line)
        if m:
            pending.append(int(m.group(1), 16))
            continue
        m = re.match(r"return \(double\) (.+);", line)
        if m:
            value = eval(m.group(1))  # literals like 1.0/3.0
            for code in pending:
                codes.append(code)
                values.append(float(value))
            pending = []
    order = sorted(range(len(codes)), key=codes.__getitem__)
    codes = [codes[i] for i in order]
    values = [values[i] for i in order]
    assert len(set(codes)) == len(codes)
    blob("NumericCodes", codes, 4)
    out.append("        internal static readonly double[] NumericValues = { " + ", ".join(repr(v) for v in values) + " };\n")

    # unicodename_db.h
    out.append(f"        internal const int NameMaxLen = {define(NAMES, 'NAME_MAXLEN')};\n")
    blob("Lexicon", numbers(body(NAMES, "lexicon")), 1)
    blob("LexiconOffset", numbers(body(NAMES, "lexicon_offset")), 4)
    out.append(f"        internal const int PhrasebookShift = {define(NAMES, 'phrasebook_shift')};\n")
    out.append(f"        internal const int PhrasebookShort = {define(NAMES, 'phrasebook_short')};\n")
    blob("Phrasebook", numbers(body(NAMES, "phrasebook")), 1)
    blob("PhrasebookOffset1", numbers(body(NAMES, "phrasebook_offset1")), 2)
    blob("PhrasebookOffset2", numbers(body(NAMES, "phrasebook_offset2")), 4)
    blob("CodeHash", numbers(body(NAMES, "code_hash")), 4)
    out.append(f"        internal const int CodeMagic = {define(NAMES, 'code_magic')};\n")
    out.append(f"        internal const int CodeSize = {define(NAMES, 'code_size')};\n")
    out.append(f"        internal const int CodePoly = {define(NAMES, 'code_poly')};\n")
    out.append(f"        internal const int AliasesStart = 0x{constant(NAMES, 'aliases_start'):X};\n")
    out.append(f"        internal const int AliasesEnd = 0x{constant(NAMES, 'aliases_end'):X};\n")
    blob("NameAliases", numbers(body(NAMES, "name_aliases")), 4)
    out.append(f"        internal const int NamedSequencesStart = 0x{constant(NAMES, 'named_sequences_start'):X};\n")
    out.append(f"        internal const int NamedSequencesEnd = 0x{constant(NAMES, 'named_sequences_end'):X};\n")
    # named_sequence {int seqlen; Py_UCS2 seq[4];}: seqlen and four code units a row
    sequences = []
    for row in re.findall(r"\{(\d+), \{([^}]*)\}\}", body(NAMES, "named_sequences")):
        units = [int(v, 0) for v in row[1].split(",")]
        sequences.append([int(row[0])] + units + [0] * (4 - len(units)))
    ints("NamedSequences", [v for row in sequences for v in row])

    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as stream:
        stream.write("// Generated by tools/gen_unicodedata.py from CPython 3.12.15's Modules/unicodedata_db.h,\n")
        stream.write("// Modules/unicodename_db.h and Objects/unicodetype_db.h. Do not edit.\n")
        stream.write("namespace SharpPy\n{\n    internal static class UnicodedataDb\n    {\n")
        stream.write("".join(out))
        stream.write("    }\n}\n")
    print(len(records) // 6, "records,", len(rows), "change records,", len(codes), "numeric,", len(sequences), "named sequences")


main()
