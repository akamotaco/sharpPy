using System;
using System.IO;
using System.IO.Compression;

namespace SharpPy
{
    /// <summary>
    /// Objects/unicodectype.c (CPython 3.12.15): the character type predicates, read from
    /// unicodetype_db.h's _PyUnicode_TypeRecords through its two-level index (gettyperecord), and
    /// the generated _PyUnicode_IsWhitespace and _PyUnicode_IsLinebreak. core/UnicodeTypeData.g.cs
    /// is that header, by tools/gen_unicode_type.py.
    /// </summary>
    internal static class UnicodeType
    {
        private const int ALPHA_MASK = 0x01;
        private const int DECIMAL_MASK = 0x02;
        private const int DIGIT_MASK = 0x04;
        private const int LOWER_MASK = 0x08;
        private const int TITLE_MASK = 0x40;
        private const int UPPER_MASK = 0x80;
        private const int XID_START_MASK = 0x100;
        private const int XID_CONTINUE_MASK = 0x200;
        private const int PRINTABLE_MASK = 0x400;
        private const int NUMERIC_MASK = 0x800;
        private const int CASE_IGNORABLE_MASK = 0x1000;
        private const int CASED_MASK = 0x2000;
        private const int EXTENDED_CASE_MASK = 0x4000;

        // _PyUnicode_TypeRecord fields, six ints a record
        private const int UPPER = 0, LOWER = 1, TITLE = 2, DECIMAL = 3, DIGIT = 4, FLAGS = 5;

        private static readonly ushort[] Index1 = Unpack(UnicodeTypeData.Index1);
        private static readonly ushort[] Index2 = Unpack(UnicodeTypeData.Index2);
        private static readonly bool[] AsciiWhitespace = MakeAsciiWhitespace();

        private static ushort[] Unpack(string text)
        {
            using var packed = new MemoryStream(Convert.FromBase64String(text));
            using var inflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var bytes = new MemoryStream();
            inflate.CopyTo(bytes);
            var raw = bytes.ToArray();
            var values = new ushort[raw.Length / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)(raw[2 * i] | (raw[2 * i + 1] << 8));
            return values;
        }

        // _Py_ascii_whitespace
        private static bool[] MakeAsciiWhitespace()
        {
            var table = new bool[128];
            foreach (int c in UnicodeTypeData.Whitespace)
                if (c < 128)
                    table[c] = true;
            return table;
        }

        private static int Flags(int code) => UnicodeTypeData.Records[Record(code) + FLAGS];

        // gettyperecord: the offset of the code point's record in Records.
        private static int Record(int code)
        {
            int index;
            if ((uint)code >= 0x110000)
                index = 0;
            else
            {
                const int shift = UnicodeTypeData.Shift;
                index = Index1[code >> shift];
                index = Index2[(index << shift) + (code & ((1 << shift) - 1))];
            }
            return index * 6;
        }

        // _PyUnicode_ToDecimalDigit, _PyUnicode_ToDigit: -1 when the character has none.
        internal static int ToDecimalDigit(int ch)
        {
            int record = Record(ch);
            return (UnicodeTypeData.Records[record + FLAGS] & DECIMAL_MASK) != 0 ? UnicodeTypeData.Records[record + DECIMAL] : -1;
        }

        internal static int ToDigit(int ch)
        {
            int record = Record(ch);
            return (UnicodeTypeData.Records[record + FLAGS] & DIGIT_MASK) != 0 ? UnicodeTypeData.Records[record + DIGIT] : -1;
        }

        // _PyUnicode_ToUppercase, _PyUnicode_ToLowercase, _PyUnicode_ToTitlecase: the simple mapping.
        private static int Simple(int ch, int field)
        {
            int record = Record(ch);
            int value = UnicodeTypeData.Records[record + field];
            if ((UnicodeTypeData.Records[record + FLAGS] & EXTENDED_CASE_MASK) != 0)
                return UnicodeTypeData.ExtendedCase[value & 0xFFFF];
            return ch + value;
        }

        internal static int ToUppercase(int ch) => Simple(ch, UPPER);
        internal static int ToLowercase(int ch) => Simple(ch, LOWER);
        internal static int ToTitlecase(int ch) => Simple(ch, TITLE);

        // _PyUnicode_ToLowerFull, _PyUnicode_ToTitleFull, _PyUnicode_ToUpperFull: up to three
        // code points into res, their count answered.
        private static int Full(int ch, int field, Span<int> res)
        {
            int record = Record(ch);
            int value = UnicodeTypeData.Records[record + field];
            if ((UnicodeTypeData.Records[record + FLAGS] & EXTENDED_CASE_MASK) != 0)
            {
                int index = value & 0xFFFF;
                int n = value >> 24;
                for (int i = 0; i < n; i++)
                    res[i] = UnicodeTypeData.ExtendedCase[index + i];
                return n;
            }
            res[0] = ch + value;
            return 1;
        }

        internal static int ToLowerFull(int ch, Span<int> res) => Full(ch, LOWER, res);
        internal static int ToTitleFull(int ch, Span<int> res) => Full(ch, TITLE, res);
        internal static int ToUpperFull(int ch, Span<int> res) => Full(ch, UPPER, res);

        // _PyUnicode_ToFoldedFull
        internal static int ToFoldedFull(int ch, Span<int> res)
        {
            int record = Record(ch);
            int lower = UnicodeTypeData.Records[record + LOWER];
            if ((UnicodeTypeData.Records[record + FLAGS] & EXTENDED_CASE_MASK) != 0 && ((lower >> 20) & 7) != 0)
            {
                int index = (lower & 0xFFFF) + (lower >> 24);
                int n = (lower >> 20) & 7;
                for (int i = 0; i < n; i++)
                    res[i] = UnicodeTypeData.ExtendedCase[index + i];
                return n;
            }
            return ToLowerFull(ch, res);
        }

        internal static bool IsAlpha(int ch) => (Flags(ch) & ALPHA_MASK) != 0;
        internal static bool IsDecimalDigit(int ch) => (Flags(ch) & DECIMAL_MASK) != 0;
        internal static bool IsDigit(int ch) => (Flags(ch) & DIGIT_MASK) != 0;
        internal static bool IsNumeric(int ch) => (Flags(ch) & NUMERIC_MASK) != 0;
        internal static bool IsLowercase(int ch) => (Flags(ch) & LOWER_MASK) != 0;
        internal static bool IsUppercase(int ch) => (Flags(ch) & UPPER_MASK) != 0;
        internal static bool IsTitlecase(int ch) => (Flags(ch) & TITLE_MASK) != 0;
        internal static bool IsXidStart(int ch) => (Flags(ch) & XID_START_MASK) != 0;
        internal static bool IsXidContinue(int ch) => (Flags(ch) & XID_CONTINUE_MASK) != 0;
        internal static bool IsPrintable(int ch) => (Flags(ch) & PRINTABLE_MASK) != 0;
        internal static bool IsCased(int ch) => (Flags(ch) & CASED_MASK) != 0;
        internal static bool IsCaseIgnorable(int ch) => (Flags(ch) & CASE_IGNORABLE_MASK) != 0;

        // Py_UNICODE_ISALNUM
        internal static bool IsAlnum(int ch) => IsAlpha(ch) || IsDecimalDigit(ch) || IsDigit(ch) || IsNumeric(ch);

        // Py_UNICODE_ISSPACE: _Py_ascii_whitespace below 128, else _PyUnicode_IsWhitespace.
        internal static bool IsSpace(int ch) =>
            ch < 128 ? ch >= 0 && AsciiWhitespace[ch] : Array.IndexOf(UnicodeTypeData.Whitespace, ch) >= 0;

        // Py_UNICODE_ISLINEBREAK
        internal static bool IsLinebreak(int ch) => Array.IndexOf(UnicodeTypeData.Linebreak, ch) >= 0;
    }
}
