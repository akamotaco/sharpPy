using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 Modules/unicodedata.c _getcode: the name to code point route of \N{...}.
    /// Names ignore case in the table, as _gethash upper-cases; the two computed families do not.
    /// </summary>
    internal static class UnicodeNames
    {
        private const string HangulPrefix = "HANGUL SYLLABLE ";
        private const string IdeographPrefix = "CJK UNIFIED IDEOGRAPH-";
        private const int SBase = 0xAC00;

        private static readonly string[] Leading = { "G", "GG", "N", "D", "DD", "R", "M", "B", "BB", "S", "SS", "", "J", "JJ", "C", "K", "T", "P", "H" };
        private static readonly string[] Vowels = { "A", "AE", "YA", "YAE", "EO", "E", "YEO", "YE", "O", "WA", "WAE", "OE", "YO", "U", "WEO", "WE", "WI", "YU", "EU", "YI", "I" };
        private static readonly string[] Trailing = { "", "G", "GG", "GS", "N", "NJ", "NH", "D", "L", "LG", "LM", "LB", "LS", "LT", "LP", "LH", "M", "B", "BS", "S", "SS", "NG", "J", "C", "K", "T", "P", "H" };

        private static Dictionary<string, int> _table;
        private static Dictionary<int, string> _names;

        /// <summary>_getucname: the name of a code point, or null.</summary>
        internal static string Name(int code)
        {
            if (code >= SBase && code < SBase + 11172)
            {
                int index = code - SBase;
                return HangulPrefix + Leading[index / 588] + Vowels[index % 588 / 28] + Trailing[index % 28];
            }
            if (IsUnifiedIdeograph(code))
                return $"{IdeographPrefix}{code:X4}";
            if (_names == null)
            {
                var names = new Dictionary<int, string>();
                foreach (var entry in Table())
                    names.TryAdd(entry.Value, entry.Key);
                _names = names;
            }
            return _names.TryGetValue(code, out var name) ? name : null;
        }

        /// <summary>The code point of a name, or -1.</summary>
        internal static int Lookup(string name)
        {
            if (name.StartsWith(HangulPrefix, StringComparison.Ordinal))
                return Hangul(name);
            if (name.StartsWith(IdeographPrefix, StringComparison.Ordinal))
                return Ideograph(name);
            return Table().TryGetValue(name.ToUpperInvariant(), out int code) ? code : -1;
        }

        private static int Hangul(string name)
        {
            int position = HangulPrefix.Length;
            int l = FindSyllable(name, ref position, Leading);
            int v = FindSyllable(name, ref position, Vowels);
            int t = FindSyllable(name, ref position, Trailing);
            if (l < 0 || v < 0 || t < 0 || position != name.Length)
                return -1;
            return SBase + (l * Vowels.Length + v) * Trailing.Length + t;
        }

        // find_syllable: the longest entry that matches here.
        private static int FindSyllable(string name, ref int position, string[] syllables)
        {
            int best = -1, length = -1;
            for (int i = 0; i < syllables.Length; i++)
            {
                string syllable = syllables[i];
                if (syllable.Length > length && string.CompareOrdinal(name, position, syllable, 0, syllable.Length) == 0)
                {
                    best = i;
                    length = syllable.Length;
                }
            }
            if (best >= 0)
                position += length;
            return best;
        }

        private static int Ideograph(string name)
        {
            int length = name.Length - IdeographPrefix.Length;
            if (length != 4 && length != 5)
                return -1;
            int code = 0;
            for (int i = IdeographPrefix.Length; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= '0' && c <= '9') code = code * 16 + (c - '0');
                else if (c >= 'A' && c <= 'F') code = code * 16 + (c - 'A' + 10);
                else return -1;
            }
            return IsUnifiedIdeograph(code) ? code : -1;
        }

        private static bool IsUnifiedIdeograph(int code) =>
            (0x3400 <= code && code <= 0x4DBF) ||
            (0x4E00 <= code && code <= 0x9FFF) ||
            (0x20000 <= code && code <= 0x2A6DF) ||
            (0x2A700 <= code && code <= 0x2B739) ||
            (0x2B740 <= code && code <= 0x2B81D) ||
            (0x2B820 <= code && code <= 0x2CEA1) ||
            (0x2CEB0 <= code && code <= 0x2EBE0) ||
            (0x30000 <= code && code <= 0x3134A) ||
            (0x31350 <= code && code <= 0x323AF);

        private static Dictionary<string, int> Table()
        {
            if (_table != null)
                return _table;
            var table = new Dictionary<string, int>(StringComparer.Ordinal);
            using var packed = new MemoryStream(Convert.FromBase64String(UnicodeNameData.Deflated));
            using var inflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var reader = new StreamReader(inflate, Encoding.ASCII);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int space = line.IndexOf(' ');
                table[line.Substring(space + 1)] = Convert.ToInt32(line.Substring(0, space), 16);
            }
            return _table = table;
        }
    }
}
