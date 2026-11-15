using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The unicode character name tables of Modules/unicodedata.c (CPython 3.12.15): _getucname over
    /// the lexicon and phrasebook, _getcode over code_hash, the Hangul syllables and the unified
    /// ideographs, and the aliases and named sequences kept in Private Use Area 15. old is the
    /// 3.2.0 database or null for the current one. Name and Lookup are its _PyUnicode_Name_CAPI
    /// (capi_getucname, capi_getcode), what \N{...} and the namereplace handler use.
    /// </summary>
    internal static class UnicodeNames
    {
        private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
        private const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

        private static readonly byte[] Lexicon = UnicodedataTables.Bytes(UnicodedataDb.Lexicon);
        private static readonly int[] LexiconOffset = UnicodedataTables.Ints(UnicodedataDb.LexiconOffset, 4);
        private static readonly byte[] Phrasebook = UnicodedataTables.Bytes(UnicodedataDb.Phrasebook);
        private static readonly int[] PhrasebookOffset1 = UnicodedataTables.Ints(UnicodedataDb.PhrasebookOffset1, 2);
        private static readonly int[] PhrasebookOffset2 = UnicodedataTables.Ints(UnicodedataDb.PhrasebookOffset2, 4);
        private static readonly int[] CodeHash = UnicodedataTables.Ints(UnicodedataDb.CodeHash, 4);
        private static readonly int[] NameAliases = UnicodedataTables.Ints(UnicodedataDb.NameAliases, 4);

        private static readonly string[,] HangulSyllables =
        {
            { "G", "A", "" }, { "GG", "AE", "G" }, { "N", "YA", "GG" }, { "D", "YAE", "GS" },
            { "DD", "EO", "N" }, { "R", "E", "NJ" }, { "M", "YEO", "NH" }, { "B", "YE", "D" },
            { "BB", "O", "L" }, { "S", "WA", "LG" }, { "SS", "WAE", "LM" }, { "", "OE", "LB" },
            { "J", "YO", "LS" }, { "JJ", "U", "LT" }, { "C", "WEO", "LP" }, { "K", "WE", "LH" },
            { "T", "WI", "M" }, { "P", "YU", "B" }, { "H", "EU", "BS" }, { null, "YI", "S" },
            { null, "I", "SS" }, { null, null, "NG" }, { null, null, "J" }, { null, null, "C" },
            { null, null, "K" }, { null, null, "T" }, { null, null, "P" }, { null, null, "H" },
        };

        // is_unified_ideograph: these ranges need to match makeunicodedata.py:cjk_ranges.
        internal static bool IsUnifiedIdeograph(int code) =>
            (0x3400 <= code && code <= 0x4DBF) ||
            (0x4E00 <= code && code <= 0x9FFF) ||
            (0x20000 <= code && code <= 0x2A6DF) ||
            (0x2A700 <= code && code <= 0x2B739) ||
            (0x2B740 <= code && code <= 0x2B81D) ||
            (0x2B820 <= code && code <= 0x2CEA1) ||
            (0x2CEB0 <= code && code <= 0x2EBE0) ||
            (0x30000 <= code && code <= 0x3134A) ||
            (0x31350 <= code && code <= 0x323AF);

        internal static bool IsAlias(int cp) => cp >= UnicodedataDb.AliasesStart && cp < UnicodedataDb.AliasesEnd;
        internal static bool IsNamedSeq(int cp) => cp >= UnicodedataDb.NamedSequencesStart && cp < UnicodedataDb.NamedSequencesEnd;

        private static char ToUpper(char c) => c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

        // _gethash
        private static uint GetHash(byte[] s, int len, int scale)
        {
            uint h = 0;
            for (int i = 0; i < len; i++)
            {
                h = (h * (uint)scale) + (byte)ToUpper((char)s[i]);
                uint ix = h & 0xff000000;
                if (ix != 0)
                    h = (h ^ ((ix >> 24) & 0xff)) & 0x00ffffff;
            }
            return h;
        }

        /// <summary>_getucname: the name of code, or null.</summary>
        internal static string GetUcName(SharpPy.Modules.UnicodedataModule.PreviousVersion old, int code, int buflen, bool withAliasAndSeq)
        {
            if ((uint)code >= 0x110000)
                return null;
            if (!withAliasAndSeq && (IsAlias(code) || IsNamedSeq(code)))
                return null;
            if (old != null)
            {
                // in 3.2.0 there are no aliases and named sequences
                if (IsAlias(code) || IsNamedSeq(code))
                    return null;
                if (old.CategoryChanged(code) == 0)
                    return null;  // unassigned
            }
            if (SBase <= code && code < SBase + SCount)
            {
                // Hangul syllable.
                int SIndex = code - SBase;
                int L = SIndex / NCount;
                int V = (SIndex % NCount) / TCount;
                int T = SIndex % TCount;
                if (buflen < 27)
                    // Worst case: HANGUL SYLLABLE <10chars>.
                    return null;
                return "HANGUL SYLLABLE " + HangulSyllables[L, 0] + HangulSyllables[V, 1] + HangulSyllables[T, 2];
            }
            if (IsUnifiedIdeograph(code))
            {
                if (buflen < 28)
                    // Worst case: CJK UNIFIED IDEOGRAPH-20000
                    return null;
                return $"CJK UNIFIED IDEOGRAPH-{code:X}";
            }
            // get offset into phrasebook
            int offset = PhrasebookOffset1[code >> UnicodedataDb.PhrasebookShift];
            offset = PhrasebookOffset2[(offset << UnicodedataDb.PhrasebookShift) + (code & ((1 << UnicodedataDb.PhrasebookShift) - 1))];
            if (offset == 0)
                return null;
            var buffer = new StringBuilder();
            int i = 0;
            while (true)
            {
                // get word index
                int word = Phrasebook[offset] - UnicodedataDb.PhrasebookShort;
                if (word >= 0)
                {
                    word = (word << 8) + Phrasebook[offset + 1];
                    offset += 2;
                }
                else
                    word = Phrasebook[offset++];
                if (i != 0)
                {
                    if (i > buflen)
                        return null;  // buffer overflow
                    buffer.Append(' ');
                    i++;
                }
                // copy word string from lexicon. the last character in the word has bit 7 set.
                // the last word in a string ends with 0x80
                int w = LexiconOffset[word];
                while (Lexicon[w] < 128)
                {
                    if (i >= buflen)
                        return null;  // buffer overflow
                    buffer.Append((char)Lexicon[w++]);
                    i++;
                }
                if (i >= buflen)
                    return null;  // buffer overflow
                buffer.Append((char)(Lexicon[w] & 127));
                i++;
                if (Lexicon[w] == 128)
                    break;  // end of word
            }
            // the final 0x80 is the string's terminator
            if (buffer.Length > 0 && buffer[^1] == '\0')
                buffer.Length--;
            return buffer.ToString();
        }

        // _cmpname: whether code's name is name, upper-cased.
        private static bool CmpName(SharpPy.Modules.UnicodedataModule.PreviousVersion old, int code, byte[] name, int namelen)
        {
            var buffer = GetUcName(old, code, UnicodedataDb.NameMaxLen, true);
            if (buffer == null || buffer.Length != namelen)
                return false;
            for (int i = 0; i < namelen; i++)
                if (ToUpper((char)name[i]) != buffer[i])
                    return false;
            return true;
        }

        // find_syllable
        private static void FindSyllable(byte[] str, int at, out int len, ref int pos, int count, int column)
        {
            len = -1;
            for (int i = 0; i < count; i++)
            {
                string s = HangulSyllables[i, column];
                int len1 = s.Length;
                if (len1 <= len)
                    continue;
                if (StartsWith(str, at, s))
                {
                    len = len1;
                    pos = i;
                }
            }
            if (len == -1)
                len = 0;
        }

        private static bool StartsWith(byte[] str, int at, string prefix)
        {
            if (at + prefix.Length > str.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
                if (str[at + i] != prefix[i])
                    return false;
            return true;
        }

        // _check_alias_and_seq
        private static bool CheckAliasAndSeq(int cp, out int code, bool withNamedSeq)
        {
            code = 0;
            // check if named sequences are allowed
            if (!withNamedSeq && IsNamedSeq(cp))
                return false;
            // if the code point is in the PUA range that we use for aliases, convert it
            code = IsAlias(cp) ? NameAliases[cp - UnicodedataDb.AliasesStart] : cp;
            return true;
        }

        /// <summary>_getcode: the code point named name, or -1.</summary>
        internal static int GetCode(SharpPy.Modules.UnicodedataModule.PreviousVersion old, byte[] name, bool withNamedSeq)
        {
            int namelen = name.Length;
            uint mask = UnicodedataDb.CodeSize - 1;
            // Check for hangul syllables.
            if (StartsWith(name, 0, "HANGUL SYLLABLE "))
            {
                int L = -1, V = -1, T = -1;
                int pos = 16;
                FindSyllable(name, pos, out int len, ref L, LCount, 0);
                pos += len;
                FindSyllable(name, pos, out len, ref V, VCount, 1);
                pos += len;
                FindSyllable(name, pos, out len, ref T, TCount, 2);
                pos += len;
                if (L != -1 && V != -1 && T != -1 && pos == namelen)
                    return SBase + (L * VCount + V) * TCount + T;
                // Otherwise, it's an illegal syllable name.
                return -1;
            }
            // Check for unified ideographs.
            if (StartsWith(name, 0, "CJK UNIFIED IDEOGRAPH-"))
            {
                // Four or five hexdigits must follow.
                int n = namelen - 22;
                if (n != 4 && n != 5)
                    return -1;
                int v = 0;
                for (int k = 22; k < namelen; k++)
                {
                    int c = name[k];
                    v *= 16;
                    if (c >= '0' && c <= '9')
                        v += c - '0';
                    else if (c >= 'A' && c <= 'F')
                        v += c - 'A' + 10;
                    else
                        return -1;
                }
                if (!IsUnifiedIdeograph(v))
                    return -1;
                return v;
            }
            // the following is the same as python's dictionary lookup, with only minor changes.
            uint h = GetHash(name, namelen, UnicodedataDb.CodeMagic);
            uint i = (~h) & mask;
            int cp = CodeHash[i];
            if (cp == 0)
                return -1;
            if (CmpName(old, cp, name, namelen))
                return CheckAliasAndSeq(cp, out int found, withNamedSeq) ? found : -1;
            uint incr = (h ^ (h >> 3)) & mask;
            if (incr == 0)
                incr = mask;
            while (true)
            {
                i = (i + incr) & mask;
                cp = CodeHash[i];
                if (cp == 0)
                    return -1;
                if (CmpName(old, cp, name, namelen))
                    return CheckAliasAndSeq(cp, out int found, withNamedSeq) ? found : -1;
                incr <<= 1;
                if (incr > mask)
                    incr ^= UnicodedataDb.CodePoly;
            }
        }

        /// <summary>capi_getucname(code, buffer, NAME_MAXLEN, 1) of the namereplace handler.</summary>
        internal static string Name(int code) => GetUcName(null, code, 256, true);

        /// <summary>capi_getcode(name, len, &amp;code, 0): \N{name} of the escape decoders, or -1.</summary>
        internal static int Lookup(string name) => GetCode(null, Encoding.Latin1.GetBytes(name), false);
    }

    /// <summary>The packed tables of core/UnicodedataDb.g.cs: raw deflate, base64, little-endian.</summary>
    internal static class UnicodedataTables
    {
        internal static byte[] Bytes(string text)
        {
            using var packed = new MemoryStream(Convert.FromBase64String(text));
            using var inflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var bytes = new MemoryStream();
            inflate.CopyTo(bytes);
            return bytes.ToArray();
        }

        internal static int[] Ints(string text, int width)
        {
            var raw = Bytes(text);
            var values = new int[raw.Length / width];
            for (int i = 0; i < values.Length; i++)
            {
                int v = 0;
                for (int b = width - 1; b >= 0; b--)
                    v = (v << 8) | raw[i * width + b];
                values[i] = v;
            }
            return values;
        }
    }
}
