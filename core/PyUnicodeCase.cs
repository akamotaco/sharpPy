using System;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 case operations, transliterated from Objects/unicodeobject.c:
    ///   do_upper / do_lower / do_title / do_capitalize / do_swapcase / do_casefold,
    ///   unicode_isupper_impl / unicode_islower_impl / unicode_istitle_impl,
    ///   plus handle_capital_sigma + lower_ucs4.
    /// The data behind them is Unicode's FULL case mappings and the cased / case-ignorable /
    /// uppercase / lowercase / titlecase properties (see core/UnicodeCaseData.cs).
    ///
    /// This exists because .NET's ToUpper/ToLowerInvariant implement only the SIMPLE (one-to-one)
    /// mapping: "ß".ToUpperInvariant() is "ß" where CPython gives "SS", U+0130 lowers to itself
    /// where CPython gives "i" + U+0307, and the Other_Uppercase / Other_Lowercase / Lt characters
    /// with no simple mapping at all (Roman numerals, circled letters, ǅ) also make isupper,
    /// islower and istitle wrong. Culture-aware ToUpper()/ToLower() were never an option.
    ///
    /// Loops run over CODE POINTS because that is what CPython's PyUnicode_READ loop indexes; ASCII
    /// text - the overwhelming majority of real strings - never enters a binary search, and a
    /// mapping that changes nothing hands back the original string instead of a copy.
    /// </summary>
    internal static class PyUnicodeCase
    {
        private static bool IsPlainAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= 0x80) return false;
            return true;
        }

        /// <summary>
        /// Decode into the caller's span and report how many code points were written. An unpaired
        /// surrogate keeps its own value, which is what CPython stores too.
        /// </summary>
        private static int FillCodePoints(string s, int[] dst)
        {
            int n = 0;
            for (int i = 0; i < s.Length;)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    dst[n++] = char.ConvertToUtf32(c, s[i + 1]);
                    i += 2;
                }
                else
                {
                    dst[n++] = c;
                    i += 1;
                }
            }
            return n;
        }

        /// <summary>
        /// Case operations run in loops over words and sentences, and the per-call code point array
        /// showed up in measurement, so the buffer is reused per thread instead of being allocated
        /// on every call.
        /// </summary>
        [ThreadStatic] private static int[] ScratchBuffer;

        private static int[] Scratch(int length)
        {
            int[] buf = ScratchBuffer;
            if (buf == null || buf.Length < length)
            {
                buf = new int[Math.Max(length, 512)];
                ScratchBuffer = buf;
            }
            return buf;
        }

        /// <summary>
        /// handle_capital_sigma: U+03A3 becomes U+03C2 when a cased character precedes it and none
        /// follows, skipping case-ignorable characters on both sides.
        /// </summary>
        /// <summary>
        /// The length is passed explicitly: the code point buffer is reused across calls, so
        /// cps.Length is the buffer, not the string.
        /// </summary>
        private static int HandleCapitalSigma(int[] cps, int i, int length)
        {
            int j;
            for (j = i - 1; j >= 0; j--)
                if (!UnicodeCaseData.IsCaseIgnorable(cps[j])) break;
            bool finalSigma = j >= 0 && UnicodeCaseData.IsCased(cps[j]);
            if (finalSigma)
            {
                for (j = i + 1; j < length; j++)
                    if (!UnicodeCaseData.IsCaseIgnorable(cps[j])) break;
                finalSigma = j == length || !UnicodeCaseData.IsCased(cps[j]);
            }
            return finalSigma ? 0x3C2 : 0x3C3;
        }

        private static void AppendCp(StringBuilder sb, int cp)
        {
            if (cp <= 0xFFFF) sb.Append((char)cp);
            else sb.Append(char.ConvertFromUtf32(cp));
        }

        /// <summary>
        /// ascii_upper_or_lower in one pass: the ASCII decision and the first character that has
        /// to move are found while filling the result buffer, so a string that needs nothing comes
        /// back as the same instance after a single scan (two scans used to cost about as much as
        /// the mapping itself). Anything above U+007F abandons the buffer and returns false.
        /// </summary>
        /// <summary>
        /// ascii_upper_or_lower: one scan decides ASCII-ness and finds the first character that has
        /// to move, and a string that needs nothing is handed back as the same instance. The general
        /// full-mapping path is only reached for text above U+007F.
        /// </summary>
        /// <summary>
        /// CPython's ascii_upper_or_lower. Above U+007F the runtime's case routines are unusable
        /// (they implement the simple mapping only), so the test comes first and the general path
        /// takes over from there. For a verified ASCII string ToUpperInvariant/ToLowerInvariant IS
        /// CPython's ASCII mapping - A-Z and a-z only, culture cannot enter - and it is vectorised.
        /// </summary>
        private static bool TryAsciiMap(string s, bool toUpper, out string result)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= 0x80)
                {
                    result = null;
                    return false;
                }
            }
            result = toUpper ? s.ToUpperInvariant() : s.ToLowerInvariant();
            return true;
        }

        internal static string Upper(string s)
        {
            if (TryAsciiMap(s, true, out string asciiResult)) return asciiResult;
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            bool changed = false;
            for (int i = 0; i < n; i++) changed |= UnicodeCaseData.AppendUpper(sb, cps[i]);
            return changed ? sb.ToString() : s;
        }

        internal static string Lower(string s)
        {
            if (TryAsciiMap(s, false, out string asciiResult)) return asciiResult;
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (cps[i] == 0x3A3) { AppendCp(sb, HandleCapitalSigma(cps, i, n)); changed = true; }
                else changed |= UnicodeCaseData.AppendLower(sb, cps[i]);
            }
            return changed ? sb.ToString() : s;
        }

        /// <summary>
        /// do_title: title-case a character when the preceding character was not cased, lower-case
        /// it otherwise. previous_is_cased reads the ORIGINAL string, so "ǅungla".title() keeps ǅ.
        /// </summary>
        internal static string Title(string s)
        {
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            bool changed = false;
            bool previousIsCased = false;
            for (int i = 0; i < n; i++)
            {
                int cp = cps[i];
                if (previousIsCased)
                {
                    if (cp == 0x3A3) { AppendCp(sb, HandleCapitalSigma(cps, i, n)); changed = true; }
                    else changed |= UnicodeCaseData.AppendLower(sb, cp);
                }
                else
                {
                    changed |= UnicodeCaseData.AppendTitle(sb, cp);
                }
                previousIsCased = UnicodeCaseData.IsCased(cp);
            }
            return changed ? sb.ToString() : s;
        }

        /// <summary>do_capitalize: first character through the TITLE mapping, the rest lowered.</summary>
        internal static string Capitalize(string s)
        {
            if (s.Length == 0) return s;
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            UnicodeCaseData.AppendTitle(sb, cps[0]);
            for (int i = 1; i < n; i++)
            {
                if (cps[i] == 0x3A3) AppendCp(sb, HandleCapitalSigma(cps, i, n));
                else UnicodeCaseData.AppendLower(sb, cps[i]);
            }
            return sb.ToString();
        }

        /// <summary>do_swapcase: uppercase lowers, lowercase uppercases, everything else stays.</summary>
        internal static string SwapCase(string s)
        {
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                int cp = cps[i];
                if (UnicodeCaseData.IsUppercase(cp))
                {
                    if (cp == 0x3A3) { AppendCp(sb, HandleCapitalSigma(cps, i, n)); changed = true; }
                    else changed |= UnicodeCaseData.AppendLower(sb, cp);
                }
                else if (UnicodeCaseData.IsLowercase(cp))
                {
                    changed |= UnicodeCaseData.AppendUpper(sb, cp);
                }
                else
                {
                    AppendCp(sb, cp);
                }
            }
            return changed ? sb.ToString() : s;
        }

        /// <summary>do_casefold: the full case-folding mapping, multi-code-point included.</summary>
        internal static string CaseFold(string s)
        {
            if (TryAsciiMap(s, false, out string asciiResult)) return asciiResult;
            int[] cps = Scratch(s.Length);
            int n = FillCodePoints(s, cps);
            var sb = new StringBuilder(n + 2);
            bool changed = false;
            for (int i = 0; i < n; i++) changed |= UnicodeCaseData.AppendFold(sb, cps[i]);
            return changed ? sb.ToString() : s;
        }

        /// <summary>unicode_isupper_impl.</summary>
        internal static bool IsUpper(string s)
        {
            bool cased = false;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = CodePointAt(s, i, out int width);
                i += width - 1;
                if (UnicodeCaseData.IsLowercase(cp) || UnicodeCaseData.IsTitlecase(cp)) return false;
                if (!cased && UnicodeCaseData.IsUppercase(cp)) cased = true;
            }
            return cased;
        }

        /// <summary>unicode_islower_impl.</summary>
        internal static bool IsLower(string s)
        {
            bool cased = false;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = CodePointAt(s, i, out int width);
                i += width - 1;
                if (UnicodeCaseData.IsUppercase(cp) || UnicodeCaseData.IsTitlecase(cp)) return false;
                if (!cased && UnicodeCaseData.IsLowercase(cp)) cased = true;
            }
            return cased;
        }

        /// <summary>unicode_istitle_impl.</summary>
        internal static bool IsTitle(string s)
        {
            bool cased = false;
            bool previousIsCased = false;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = CodePointAt(s, i, out int width);
                i += width - 1;
                if (UnicodeCaseData.IsUppercase(cp) || UnicodeCaseData.IsTitlecase(cp))
                {
                    if (previousIsCased) return false;
                    previousIsCased = true;
                    cased = true;
                }
                else if (UnicodeCaseData.IsLowercase(cp))
                {
                    if (!previousIsCased) return false;
                    previousIsCased = true;
                    cased = true;
                }
                else
                {
                    previousIsCased = false;
                }
            }
            return cased;
        }

        private static int CodePointAt(string s, int i, out int width)
        {
            char c = s[i];
            if (c < 0x80) { width = 1; return c; }
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(c, s[i + 1]);
            }
            width = 1;
            return c;
        }
    }
}
