using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/unicodedata.c (CPython 3.12.15) over core/UnicodedataDb.g.cs, which is its
    /// unicodedata_db.h and unicodename_db.h. Each function is both a module function and a method
    /// of unicodedata.UCD: a module function has no previous version (UCD_Check is false), the
    /// method of ucd_3_2_0 reads the 3.2.0 change records over the current data.
    /// </summary>
    internal static class UnicodedataModule
    {
        // _PyUnicode_DatabaseRecord: six bytes a record
        private const int CATEGORY = 0, COMBINING = 1, BIDIRECTIONAL = 2, MIRRORED = 3, EAST_ASIAN_WIDTH = 4, QUICK_CHECK = 5;

        private static readonly byte[] Records = UnicodedataTables.Bytes(UnicodedataDb.Records);
        private static readonly int[] Index1 = UnicodedataTables.Ints(UnicodedataDb.Index1, 2);
        private static readonly int[] Index2 = UnicodedataTables.Ints(UnicodedataDb.Index2, 2);
        private static readonly int[] DecompData = UnicodedataTables.Ints(UnicodedataDb.DecompData, 4);
        private static readonly byte[] DecompIndex1 = UnicodedataTables.Bytes(UnicodedataDb.DecompIndex1);
        private static readonly int[] DecompIndex2 = UnicodedataTables.Ints(UnicodedataDb.DecompIndex2, 2);
        private static readonly int[] CompIndex = UnicodedataTables.Ints(UnicodedataDb.CompIndex, 2);
        private static readonly int[] CompData = UnicodedataTables.Ints(UnicodedataDb.CompData, 4);
        private static readonly byte[] Changes320Index = UnicodedataTables.Bytes(UnicodedataDb.Changes320Index);
        private static readonly byte[] Changes320Data = UnicodedataTables.Bytes(UnicodedataDb.Changes320Data);
        private static readonly int[] NumericCodes = UnicodedataTables.Ints(UnicodedataDb.NumericCodes, 4);

        private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
        private const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

        // _getrecord_ex: the offset of the code point's record.
        private static int Record(int code)
        {
            int index;
            if ((uint)code >= 0x110000)
                index = 0;
            else
            {
                const int shift = UnicodedataDb.Shift;
                index = Index1[code >> shift];
                index = Index2[(index << shift) + (code & ((1 << shift) - 1))];
            }
            return index * 6;
        }

        internal static int Combining(int code) => Records[Record(code) + COMBINING];

        // _PyUnicode_ToNumeric: -1.0 when the character has no numeric value.
        private static double ToNumeric(int ch)
        {
            int at = Array.BinarySearch(NumericCodes, ch);
            return at >= 0 ? UnicodedataDb.NumericValues[at] : -1.0;
        }

        /// <summary>
        /// PreviousDBVersion: a UCD with the change records of an older database (get_change_3_2_0)
        /// and its normalization changes (normalization_3_2_0).
        /// </summary>
        internal sealed class PreviousVersion : PyObject
        {
            internal readonly string Name;

            internal PreviousVersion(string name) => Name = name;

            public override PyType GetPyType() => UcdType;

            // get_change_3_2_0: the change record's index
            private static int Change(int n)
            {
                int index;
                if ((uint)n >= 0x110000)
                    index = 0;
                else
                {
                    index = Changes320Index[n >> 7];
                    index = Changes320Data[(index << 7) + (n & 127)];
                }
                return index;
            }

            private static int Field(int n, int field) => UnicodedataDb.ChangeRecordBytes[Change(n) * 5 + field];
            internal int BidirChanged(int n) => Field(n, 0);
            internal int CategoryChanged(int n) => Field(n, 1);
            internal int DecimalChanged(int n) => Field(n, 2);
            internal int MirroredChanged(int n) => Field(n, 3);
            internal int EastAsianWidthChanged(int n) => Field(n, 4);

            // normalization_3_2_0
            internal static int Normalization(int n)
            {
                var pairs = UnicodedataDb.Normalization320;
                for (int i = 0; i < pairs.Length; i += 2)
                    if (pairs[i] == n)
                        return pairs[i + 1];
                return 0;
            }
        }

        internal static readonly PyType UcdType = MakeUcdType();

        // chr: int(accept={str}), a str of one character
        private static int Chr(string fname, string displayname, PyObject arg)
        {
            if (arg is not PyString text || text.CodePointLength() != 1)
                throw PyArgs.BadArgument(fname, displayname, "a unicode character", arg);
            return PyString.CodePointAt(text.Value, 0, out _);
        }

        private static PyObject Default(PyObject defaultValue, string message) =>
            defaultValue ?? throw PyValueError.Create(message);

        // unicodedata_UCD_decimal_impl
        private static PyObject Decimal(PreviousVersion self, int c, PyObject defaultValue)
        {
            bool haveOld = false;
            long rc = 0;
            if (self != null)
            {
                if (self.CategoryChanged(c) == 0)
                {
                    // unassigned
                    haveOld = true;
                    rc = -1;
                }
                else if (self.DecimalChanged(c) != 0xFF)
                {
                    haveOld = true;
                    rc = self.DecimalChanged(c);
                }
            }
            if (!haveOld)
                rc = UnicodeType.ToDecimalDigit(c);
            if (rc < 0)
                return Default(defaultValue, "not a decimal");
            return PyInt.From(rc);
        }

        // unicodedata_UCD_digit_impl
        private static PyObject Digit(PreviousVersion self, int c, PyObject defaultValue)
        {
            long rc = UnicodeType.ToDigit(c);
            if (rc < 0)
                return Default(defaultValue, "not a digit");
            return PyInt.From(rc);
        }

        // unicodedata_UCD_numeric_impl
        private static PyObject Numeric(PreviousVersion self, int c, PyObject defaultValue)
        {
            bool haveOld = false;
            double rc = 0;
            if (self != null)
            {
                if (self.CategoryChanged(c) == 0)
                {
                    // unassigned
                    haveOld = true;
                    rc = -1.0;
                }
                else if (self.DecimalChanged(c) != 0xFF)
                {
                    haveOld = true;
                    rc = self.DecimalChanged(c);
                }
            }
            if (!haveOld)
                rc = ToNumeric(c);
            if (rc == -1.0)
                return Default(defaultValue, "not a numeric character");
            return new PyFloat(rc);
        }

        // unicodedata_UCD_category_impl
        private static PyObject Category(PreviousVersion self, int c)
        {
            int index = Records[Record(c) + CATEGORY];
            if (self != null && self.CategoryChanged(c) != 0xFF)
                index = self.CategoryChanged(c);
            return PyString.From(UnicodedataDb.CategoryNames[index]);
        }

        // unicodedata_UCD_bidirectional_impl
        private static PyObject Bidirectional(PreviousVersion self, int c)
        {
            int index = Records[Record(c) + BIDIRECTIONAL];
            if (self != null)
            {
                if (self.CategoryChanged(c) == 0)
                    index = 0;  // unassigned
                else if (self.BidirChanged(c) != 0xFF)
                    index = self.BidirChanged(c);
            }
            return PyString.From(UnicodedataDb.BidirectionalNames[index]);
        }

        // unicodedata_UCD_combining_impl
        private static PyObject CombiningImpl(PreviousVersion self, int c)
        {
            int index = Records[Record(c) + COMBINING];
            if (self != null && self.CategoryChanged(c) == 0)
                index = 0;  // unassigned
            return PyInt.From(index);
        }

        // unicodedata_UCD_mirrored_impl
        private static PyObject Mirrored(PreviousVersion self, int c)
        {
            int index = Records[Record(c) + MIRRORED];
            if (self != null)
            {
                if (self.CategoryChanged(c) == 0)
                    index = 0;  // unassigned
                else if (self.MirroredChanged(c) != 0xFF)
                    index = self.MirroredChanged(c);
            }
            return PyInt.From(index);
        }

        // unicodedata_UCD_east_asian_width_impl
        private static PyObject EastAsianWidth(PreviousVersion self, int c)
        {
            int index = Records[Record(c) + EAST_ASIAN_WIDTH];
            if (self != null)
            {
                if (self.CategoryChanged(c) == 0)
                    index = 0;  // unassigned
                else if (self.EastAsianWidthChanged(c) != 0xFF)
                    index = self.EastAsianWidthChanged(c);
            }
            return PyString.From(UnicodedataDb.EastAsianWidthNames[index]);
        }

        private static int DecompIndex(int code)
        {
            if ((uint)code >= 0x110000)
                return 0;
            const int shift = UnicodedataDb.DecompShift;
            int index = DecompIndex1[code >> shift];
            return DecompIndex2[(index << shift) + (code & ((1 << shift) - 1))];
        }

        // unicodedata_UCD_decomposition_impl
        private static PyObject Decomposition(PreviousVersion self, int c)
        {
            if (self != null && self.CategoryChanged(c) == 0)
                return PyString.From("");  // unassigned
            int index = DecompIndex(c);
            // high byte is number of hex bytes (usually one or two), low byte is prefix code
            int count = DecompData[index] >> 8;
            int prefixIndex = DecompData[index] & 255;
            var decomp = new StringBuilder(UnicodedataDb.DecompPrefix[prefixIndex]);
            while (count-- > 0)
            {
                if (decomp.Length != 0)
                    decomp.Append(' ');
                decomp.Append(DecompData[++index].ToString("X4"));
            }
            return PyString.From(decomp.ToString());
        }

        // get_decomp_record
        private static void GetDecompRecord(PreviousVersion self, int code, out int index, out int prefix, out int count)
        {
            if ((uint)code >= 0x110000)
                index = 0;
            else if (self != null && self.CategoryChanged(code) == 0)
                index = 0;  // unassigned in old version
            else
                index = DecompIndex(code);
            count = DecompData[index] >> 8;
            prefix = DecompData[index] & 255;
            index++;
        }

        // nfd_nfkd
        private static int[] NfdNfkd(PreviousVersion self, int[] input, bool k)
        {
            var output = new List<int>(input.Length + 10);
            var stack = new Stack<int>();
            foreach (int ch in input)
            {
                stack.Push(ch);
                while (stack.Count != 0)
                {
                    int code = stack.Pop();
                    // Hangul Decomposition.
                    if (SBase <= code && code < SBase + SCount)
                    {
                        int SIndex = code - SBase;
                        int L = LBase + SIndex / NCount;
                        int V = VBase + (SIndex % NCount) / TCount;
                        int T = TBase + SIndex % TCount;
                        output.Add(L);
                        output.Add(V);
                        if (T != TBase)
                            output.Add(T);
                        continue;
                    }
                    // normalization changes
                    if (self != null)
                    {
                        int value = PreviousVersion.Normalization(code);
                        if (value != 0)
                        {
                            stack.Push(value);
                            continue;
                        }
                    }
                    // Other decompositions.
                    GetDecompRecord(self, code, out int index, out int prefix, out int count);
                    // Copy character if it is not decomposable, or has a compatibility
                    // decomposition, but we do NFD.
                    if (count == 0 || (prefix != 0 && !k))
                    {
                        output.Add(code);
                        continue;
                    }
                    // Copy decomposition onto the stack, in reverse order.
                    while (count != 0)
                        stack.Push(DecompData[index + (--count)]);
                }
            }
            var result = output.ToArray();
            // Sort each consecutive combining-character run canonically (a stable sort by class).
            int i = 0, o = result.Length;
            while (i < o)
            {
                int prev = Combining(result[i]);
                if (prev == 0)
                {
                    i++;
                    continue;
                }
                int runStart = i++;
                bool needsSort = false;
                while (i < o)
                {
                    int cur = Combining(result[i]);
                    if (cur == 0)
                        break;
                    if (prev > cur)
                        needsSort = true;
                    prev = cur;
                    i++;
                }
                if (!needsSort)
                    continue;
                // canonical_ordering_sort_insertion (the counting sort gives the same order)
                for (int a = runStart + 1; a < i; a++)
                {
                    int code = result[a];
                    int combining = Combining(code);
                    int j = a;
                    while (j > runStart && Combining(result[j - 1]) > combining)
                    {
                        result[j] = result[j - 1];
                        j--;
                    }
                    result[j] = code;
                }
            }
            return result;
        }

        // find_nfc_index over struct reindex {start, count, index}, ending at start 0
        private static int FindNfcIndex(int[] nfc, int code)
        {
            for (int row = 0; nfc[row * 3] != 0; row++)
            {
                int start = nfc[row * 3];
                if (code < start)
                    return -1;
                if (code <= start + nfc[row * 3 + 1])
                    return nfc[row * 3 + 2] + (code - start);
            }
            return -1;
        }

        // nfc_nfkc
        private static int[] NfcNfkc(PreviousVersion self, int[] input, bool k)
        {
            var data = NfdNfkd(self, input, k);
            int len = data.Length;
            var output = new int[len];
            var skipped = new List<int>();
            int i = 0, o = 0;
            while (i < len)
            {
                int skip = skipped.IndexOf(i);
                if (skip >= 0)
                {
                    // the character is skipped. Remove from list.
                    skipped[skip] = skipped[^1];
                    skipped.RemoveAt(skipped.Count - 1);
                    i++;
                    continue;
                }
                // Hangul Composition. We don't need to check for <LV,T> pairs, since we always
                // have decomposed data.
                int code = data[i];
                if (LBase <= code && code < LBase + LCount && i + 1 < len &&
                    VBase <= data[i + 1] && data[i + 1] < VBase + VCount)
                {
                    int LIndex = code - LBase;
                    int VIndex = data[i + 1] - VBase;
                    code = SBase + (LIndex * VCount + VIndex) * TCount;
                    i += 2;
                    if (i < len && TBase < data[i] && data[i] < TBase + TCount)
                    {
                        code += data[i] - TBase;
                        i++;
                    }
                    output[o++] = code;
                    continue;
                }
                // code is still input[i] here
                int f = FindNfcIndex(UnicodedataDb.NfcFirst, code);
                if (f == -1)
                {
                    output[o++] = code;
                    i++;
                    continue;
                }
                // Find next unblocked character.
                int i1 = i + 1;
                int comb = 0;
                // output base character for now; might be updated later.
                output[o] = data[i];
                while (i1 < len)
                {
                    int code1 = data[i1];
                    int comb1 = Combining(code1);
                    if (comb != 0)
                    {
                        if (comb1 == 0)
                            break;
                        if (comb >= comb1)
                        {
                            // Character is blocked.
                            i1++;
                            continue;
                        }
                    }
                    int l = FindNfcIndex(UnicodedataDb.NfcLast, code1);
                    int composed = 0;
                    if (l != -1)
                    {
                        int index = f * UnicodedataDb.TotalLast + l;
                        int index1 = CompIndex[index >> UnicodedataDb.CompShift];
                        composed = CompData[(index1 << UnicodedataDb.CompShift) + (index & ((1 << UnicodedataDb.CompShift) - 1))];
                    }
                    if (composed == 0)
                    {
                        // not_combinable: i1 cannot be combined with i. If i1 is a starter, we
                        // don't need to look further. Otherwise, record the combining class.
                        if (comb1 == 0)
                            break;
                        comb = comb1;
                        i1++;
                        continue;
                    }
                    // Replace the original character.
                    output[o] = composed;
                    // Mark the second character unused.
                    skipped.Add(i1);
                    i1++;
                    f = FindNfcIndex(UnicodedataDb.NfcFirst, output[o]);
                    if (f == -1)
                        break;
                }
                // Output character was already written. Just advance the indices.
                o++;
                i++;
            }
            if (o == len)
                // No changes. Return original string.
                return data;
            Array.Resize(ref output, o);
            return output;
        }

        private enum Quickcheck { Yes = 0, Maybe = 1, No = 2 }

        // is_normalized_quickcheck
        private static Quickcheck IsNormalizedQuickcheck(PreviousVersion self, string input, int[] data, bool nfc, bool k, bool yesOnly)
        {
            // UCD 3.2.0 is requested, quickchecks must be disabled.
            if (self != null)
                return Quickcheck.Maybe;
            bool ascii = true;
            foreach (char c in input)
                if (c >= 128) { ascii = false; break; }
            if (ascii)
                return Quickcheck.Yes;
            int prevCombining = 0;
            // The two quickcheck bits at this shift have type QuickcheckResult.
            int quickcheckShift = (nfc ? 4 : 0) + (k ? 2 : 0);
            var result = Quickcheck.Yes;
            foreach (int ch in data)
            {
                int record = Record(ch);
                int combining = Records[record + COMBINING];
                if (combining != 0 && prevCombining > combining)
                    return Quickcheck.No;  // non-canonical sort order, not normalized
                prevCombining = combining;
                int quickcheckWhole = Records[record + QUICK_CHECK];
                if (yesOnly)
                {
                    if ((quickcheckWhole & (3 << quickcheckShift)) != 0)
                        return Quickcheck.Maybe;
                }
                else
                {
                    switch ((Quickcheck)((quickcheckWhole >> quickcheckShift) & 3))
                    {
                        case Quickcheck.No:
                            return Quickcheck.No;
                        case Quickcheck.Maybe:
                            result = Quickcheck.Maybe;  // this string might need normalization
                            break;
                    }
                }
            }
            return result;
        }

        private static string Text(int[] points)
        {
            var sb = new StringBuilder(points.Length);
            foreach (int cp in points)
            {
                if (cp < 0x10000) sb.Append((char)cp);
                else sb.Append(char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
        }

        private static bool Form(string form, out bool nfc, out bool k)
        {
            nfc = form is "NFC" or "NFKC";
            k = form is "NFKC" or "NFKD";
            return form is "NFC" or "NFKC" or "NFD" or "NFKD";
        }

        // unicodedata_UCD_is_normalized_impl
        private static PyObject IsNormalized(PreviousVersion self, PyString form, PyString input)
        {
            if (input.Value.Length == 0)
                // special case empty input strings.
                return PyBool.True;
            if (!Form(form.Value, out bool nfc, out bool k))
                throw PyValueError.Create("invalid normalization form");
            var data = UnicodeEncoders.CodePoints(input.Value);
            var m = IsNormalizedQuickcheck(self, input.Value, data, nfc, k, false);
            if (m == Quickcheck.Maybe)
            {
                var cmp = nfc ? NfcNfkc(self, data, k) : NfdNfkd(self, data, k);
                return PyBool.FromBool(Text(cmp) == input.Value);
            }
            return PyBool.FromBool(m == Quickcheck.Yes);
        }

        // unicodedata_UCD_normalize_impl
        private static PyObject Normalize(PreviousVersion self, PyString form, PyString input)
        {
            if (input.Value.Length == 0)
                // Special case empty input strings.
                return input;
            if (!Form(form.Value, out bool nfc, out bool k))
                throw PyValueError.Create("invalid normalization form");
            var data = UnicodeEncoders.CodePoints(input.Value);
            if (IsNormalizedQuickcheck(self, input.Value, data, nfc, k, true) == Quickcheck.Yes)
                return input;
            return PyString.From(Text(nfc ? NfcNfkc(self, data, k) : NfdNfkd(self, data, k)));
        }

        // unicodedata_UCD_name_impl
        private static PyObject Name(PreviousVersion self, int c, PyObject defaultValue)
        {
            var name = UnicodeNames.GetUcName(self, c, UnicodedataDb.NameMaxLen, false);
            if (name == null)
                return Default(defaultValue, "no such name");
            return PyString.From(name);
        }

        // unicodedata_UCD_lookup: PyArg_Parse "s#:lookup", a str's UTF-8 or a read-only buffer.
        private static PyObject Lookup(PreviousVersion self, PyObject arg)
        {
            // convertbuffer: a buffer must be read-only (getbuffer refuses a writable one), and
            // anything that is no buffer at all is PyObject_GetBuffer's refusal.
            byte[] name;
            if (arg is PyString text)
                name = text.Encode("utf-8").Value;
            else if (arg is PyBytes bytes)
                name = bytes.Value;
            else if (arg is PyByteArray || arg is PyMemoryView { ReadOnly: false })
                throw PyTypeError.Create($"lookup() argument must be read-only bytes-like object, not {arg.GetPyType().FullName}");
            else
                name = BytesMethods.Buffer(arg) ?? throw PyTypeError.Create($"a bytes-like object is required, not '{arg.GetPyType().FullName}'");
            if (name.Length > UnicodedataDb.NameMaxLen)
                throw PyKeyError.Create(PyString.From("name too long"));
            int code = UnicodeNames.GetCode(self, name, true);
            if (code < 0)
            {
                int nul = Array.IndexOf(name, (byte)0);
                string shown = Encoding.UTF8.GetString(name, 0, nul < 0 ? name.Length : nul);
                throw PyKeyError.Create(PyString.From($"undefined character name '{shown}'"));
            }
            // check if code is in the PUA range that we use for named sequences and convert it
            if (UnicodeNames.IsNamedSeq(code))
            {
                int index = code - UnicodedataDb.NamedSequencesStart;
                var seq = UnicodedataDb.NamedSequences;
                var sb = new StringBuilder();
                for (int i = 0; i < seq[index * 5]; i++)
                    sb.Append((char)seq[index * 5 + 1 + i]);
                return PyString.From(sb.ToString());
            }
            return PyString.From(PyString.CodePointText(code));
        }

        private static PyString StrArgument(string fname, string displayname, PyObject arg) =>
            arg as PyString ?? throw PyArgs.BadArgument(fname, displayname, "str", arg);

        // unicodedata_functions: each with its calling convention; self is the UCD or null.
        private static void Install(Action<string, int, int, Func<PreviousVersion, PyObject[], PyObject>> define)
        {
            define("decimal", 1, 2, (s, a) => Decimal(s, Chr("decimal", "argument 1", a[0]), a.Length > 1 ? a[1] : null));
            define("digit", 1, 2, (s, a) => Digit(s, Chr("digit", "argument 1", a[0]), a.Length > 1 ? a[1] : null));
            define("numeric", 1, 2, (s, a) => Numeric(s, Chr("numeric", "argument 1", a[0]), a.Length > 1 ? a[1] : null));
            define("category", -1, -1, (s, a) => Category(s, Chr("category", "argument", a[0])));
            define("bidirectional", -1, -1, (s, a) => Bidirectional(s, Chr("bidirectional", "argument", a[0])));
            define("combining", -1, -1, (s, a) => CombiningImpl(s, Chr("combining", "argument", a[0])));
            define("mirrored", -1, -1, (s, a) => Mirrored(s, Chr("mirrored", "argument", a[0])));
            define("east_asian_width", -1, -1, (s, a) => EastAsianWidth(s, Chr("east_asian_width", "argument", a[0])));
            define("decomposition", -1, -1, (s, a) => Decomposition(s, Chr("decomposition", "argument", a[0])));
            define("name", 1, 2, (s, a) => Name(s, Chr("name", "argument 1", a[0]), a.Length > 1 ? a[1] : null));
            define("lookup", -1, -1, (s, a) => Lookup(s, a[0]));
            define("is_normalized", 2, 2, (s, a) => IsNormalized(s, StrArgument("is_normalized", "argument 1", a[0]), StrArgument("is_normalized", "argument 2", a[1])));
            define("normalize", 2, 2, (s, a) => Normalize(s, StrArgument("normalize", "argument 1", a[0]), StrArgument("normalize", "argument 2", a[1])));
        }

        // ucd_type_spec: unicodedata.UCD, DISALLOW_INSTANTIATION, its methods and unidata_version.
        private static PyType MakeUcdType()
        {
            var type = new PyType("UCD", new[] { PyType.ObjectType }, "unicodedata");
            type.MarkSpecHeapType();
            type.TypeDict["__module__"] = PyString.From("unicodedata");
            Install((name, min, max, body) =>
            {
                // min -1 is METH_O; otherwise METH_FASTCALL with _PyArg_CheckPositional
                if (min < 0)
                    PyArgs.Define(type, name, PyCall.One, (self, args) => body((PreviousVersion)self, args));
                else
                    PyArgs.Define(type, name, PyCall.Fast, (self, args) => body((PreviousVersion)self, args), min, max);
            });
            // DB_members: {"unidata_version", T_STRING, READONLY}
            type.TypeDict["unidata_version"] = new PyMemberDescriptor("unidata_version", type,
                self => PyString.From(((PreviousVersion)self).Name), readOnly: true);
            return type;
        }

        internal static PyModule CreateModule()
        {
            var module = new PyModule("unicodedata");
            Install((name, min, max, body) =>
                module.ModuleDict[name] = min < 0
                    ? PyArgs.MethO("unicodedata", name, args => body(null, args))
                    : PyArgs.FastCall("unicodedata", name, min, max, args => body(null, args)));
            // unicodedata_exec
            module.ModuleDict["unidata_version"] = PyString.From(UnicodedataDb.UnidataVersion);
            module.ModuleDict["UCD"] = UcdType;
            // Unicode database version 3.2.0 used by the IDNA encoding
            module.ModuleDict["ucd_3_2_0"] = new PreviousVersion("3.2.0");
            // Export C API
            module.ModuleDict["_ucnhash_CAPI"] = new PyCapsule("unicodedata._ucnhash_CAPI", null);
            return module;
        }
    }
}
